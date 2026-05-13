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

#nullable enable

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Static helpers for certificate validation. These helpers were
    /// previously private/internal members of the legacy
    /// <c>CertificateValidator</c> class; they are exposed here so that
    /// modern callers (e.g. <see cref="CertificateValidationCore"/>,
    /// <see cref="CertificateValidationExtensions"/>) can share them
    /// without depending on the obsolete type.
    /// </summary>
    public static class CertificateValidationHelpers
    {
        /// <summary>
        /// Dictionary of named curves and their bit sizes.
        /// </summary>
        internal static readonly Dictionary<string, int> NamedCurveBitSizes = new()
        {
            // NIST Curves
            { ECCurve.NamedCurves.nistP256.Oid.Value ?? "1.2.840.10045.3.1.7", 256 }, // NIST P-256
            { ECCurve.NamedCurves.nistP384.Oid.Value ?? "1.3.132.0.34", 384 }, // NIST P-384
            { ECCurve.NamedCurves.nistP521.Oid.Value ?? "1.3.132.0.35", 521 }, // NIST P-521
            // Brainpool Curves
            { ECCurve.NamedCurves.brainpoolP256r1.Oid.Value ?? "1.3.36.3.3.2.8.1.1.7", 256 }, // BrainpoolP256r1
            { ECCurve.NamedCurves.brainpoolP384r1.Oid.Value ?? "1.3.36.3.3.2.8.1.1.11", 384 } // BrainpoolP384r1
        };

        /// <summary>
        /// Returns if a certificate is signed with a SHA1 algorithm.
        /// </summary>
        internal static bool IsSHA1SignatureAlgorithm(Oid oid)
        {
            return oid.Value
                is "1.3.14.3.2.29"
                    or // sha1RSA
                    "1.2.840.10040.4.3"
                    or // sha1DSA
                    Oids.ECDsaWithSha1
                    or // sha1ECDSA
                    "1.2.840.113549.1.1.5"
                    or // sha1RSA
                    "1.3.14.3.2.13"
                    or // sha1DSA
                    "1.3.14.3.2.27"; // dsaSHA1
        }

        /// <summary>
        /// Returns if a self signed certificate is properly signed.
        /// </summary>
        internal static bool IsSignatureValid(Certificate cert)
        {
            return X509Utils.VerifySelfSigned(cert);
        }

        /// <summary>
        /// Find the domain in a certificate in the
        /// endpoint that was used to connect a session.
        /// </summary>
        /// <param name="serverCertificate">The server certificate which is tested for domain names.</param>
        /// <param name="endpointUrl">The endpoint Url which was used to connect.</param>
        /// <returns>True if domain was found.</returns>
        internal static bool FindDomain(Certificate serverCertificate, Uri endpointUrl)
        {
            bool domainFound = false;

            // check the certificate domains.
            ArrayOf<string> domains = X509Utils.GetDomainsFromCertificate(serverCertificate);

            if (!domains.IsEmpty)
            {
                string hostname;
                string dnsHostName = hostname = endpointUrl.IdnHost;
                bool isLocalHost = false;
                if (endpointUrl.HostNameType == UriHostNameType.Dns)
                {
                    if (string.Equals(dnsHostName, "localhost", StringComparison.OrdinalIgnoreCase))
                    {
                        isLocalHost = true;
                    }
                    else
                    {
                        // strip domain names from hostname
                        hostname = dnsHostName.Split('.')[0];
                    }
                }
                else
                {
                    // dnsHostname is a IPv4 or IPv6 address
                    // normalize ip addresses, cert parser returns normalized addresses
                    hostname = Utils.NormalizedIPAddress(dnsHostName);
                    if (hostname is "127.0.0.1" or "::1")
                    {
                        isLocalHost = true;
                    }
                }

                if (isLocalHost)
                {
                    dnsHostName = Utils.GetFullQualifiedDomainName();
                    hostname = Utils.GetHostName();
                }

                for (int ii = 0; ii < domains.Count; ii++)
                {
                    if (string.Equals(hostname, domains[ii], StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dnsHostName, domains[ii], StringComparison.OrdinalIgnoreCase))
                    {
                        domainFound = true;
                        break;
                    }
                }
            }
            return domainFound;
        }

        /// <summary>
        /// Returns if the certificate is secure enough for the profile.
        /// </summary>
        /// <param name="certificate">The certificate to check.</param>
        /// <param name="requiredKeySizeInBits">The required key size in bits.</param>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        public static bool IsECSecureForProfile(
            Certificate certificate,
            int requiredKeySizeInBits)
        {
            using ECDsa ecdsa =
                certificate.GetECDsaPublicKey()
                ?? throw new ArgumentException("Certificate does not contain an ECC public key");

            if (ecdsa.KeySize != 0)
            {
                return ecdsa.KeySize >= requiredKeySizeInBits;
            }
            ECCurve curve = ecdsa.ExportParameters(false).Curve;

            if (curve.IsNamed)
            {
                if (NamedCurveBitSizes.TryGetValue(curve.Oid.Value!, out int curveSize))
                {
                    return curveSize >= requiredKeySizeInBits;
                }
                throw new NotSupportedException($"Unknown named curve: {curve.Oid.Value}");
            }

            throw new NotSupportedException("Unsupported curve type.");
        }

        /// <summary>
        /// Validates that the application URI in the supplied
        /// <paramref name="serverCertificate"/> matches the application URI
        /// in the endpoint description.
        /// </summary>
        /// <param name="serverCertificate">The server certificate.</param>
        /// <param name="endpoint">The endpoint used to connect.</param>
        /// <returns>
        /// <see cref="ServiceResult.Good"/> on success; otherwise a
        /// <see cref="StatusCodes.BadCertificateUriInvalid"/> result
        /// describing the mismatch.
        /// </returns>
        public static ServiceResult ValidateServerCertificateApplicationUri(
            Certificate serverCertificate,
            ConfiguredEndpoint endpoint)
        {
            string? applicationUri = endpoint?.Description?.Server?.ApplicationUri;

            // check that an ApplicatioUri is specified for the Endpoint
            if (string.IsNullOrEmpty(applicationUri))
            {
                return ServiceResult.Create(
                    StatusCodes.BadCertificateUriInvalid,
                    "Server did not return an ApplicationUri in the EndpointDescription.");
            }

            // Check if the application URI matches any URI in the certificate
            // and get the list of certificate URIs in a single call
            if (!X509Utils.CompareApplicationUriWithCertificate(
                serverCertificate,
                applicationUri!,
                out IReadOnlyList<string> certificateApplicationUris))
            {
                if (certificateApplicationUris.Count == 0)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadCertificateUriInvalid,
                        "The Server Certificate ({0}) does not contain an applicationUri.",
                        serverCertificate.Subject);
                }

                return ServiceResult.Create(
                    StatusCodes.BadCertificateUriInvalid,
                    "The Application in the EndpointDescription ({0}) is not in the Server Certificate ({1}).",
                    applicationUri, serverCertificate.Subject);
            }

            return ServiceResult.Good;
        }
    }
}
