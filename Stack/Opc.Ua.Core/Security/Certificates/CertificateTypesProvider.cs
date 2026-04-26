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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// The provider for the X509 application certificates.
    /// </summary>
    public class CertificateTypesProvider
    {
        /// <summary>
        /// Disallow to create types provider without configuration.
        /// </summary>
        private CertificateTypesProvider()
        {
            m_securityConfiguration = null!;
            m_certificateValidator = null!;
            m_certificateChain = null!;
        }

        /// <summary>
        /// Create an instance of the certificate provider.
        /// </summary>
        public CertificateTypesProvider(ApplicationConfiguration config, ITelemetryContext telemetry)
        {
            m_securityConfiguration = config.SecurityConfiguration;
            m_certificateValidator = new CertificateValidator(telemetry);
            m_certificateChain
                = new ConcurrentDictionary<string, Tuple<CertificateCollection, byte[]>>();
        }

        /// <summary>
        /// Initialize the certificate Validator.
        /// </summary>
        public async Task InitializeAsync()
        {
            await m_certificateValidator.UpdateAsync(m_securityConfiguration).ConfigureAwait(false);

            // for application certificates, allow untrusted and revocation status unknown, cache the known certs
            m_certificateValidator.RejectUnknownRevocationStatus = false;
            m_certificateValidator.AutoAcceptUntrustedCertificates = true;
            m_certificateValidator.UseValidatedCertificates = true;

            m_certificateChain.Clear();
        }

        /// <summary>
        /// Gets or sets a value indicating whether the application should send the complete certificate chain.
        /// </summary>
        /// <remarks>
        /// If set to true the complete certificate chain will be sent for CA signed certificates.
        /// </remarks>
        public bool SendCertificateChain => m_securityConfiguration.SendCertificateChain;

        /// <summary>
        /// Return the instance certificate for a security policy.
        /// </summary>
        /// <param name="securityPolicyUri">The security policy Uri</param>
        public Certificate? GetInstanceCertificate(string securityPolicyUri)
        {
            if (securityPolicyUri == SecurityPolicies.None)
            {
                // return the default certificate for None
                return (m_securityConfiguration.ApplicationCertificates.ToArray() ?? []).FirstOrDefault()?.Certificate;
            }
            foreach (NodeId certType in Ua.CertificateIdentifier
                .MapSecurityPolicyToCertificateTypes(securityPolicyUri))
            {
                Ua.CertificateIdentifier? instanceCertificate =
                    (m_securityConfiguration.ApplicationCertificates.ToArray() ?? []).FirstOrDefault(id =>
                        id.CertificateType == certType);
                if (instanceCertificate == null &&
                    certType == ObjectTypeIds.RsaSha256ApplicationCertificateType)
                {
                    instanceCertificate = (m_securityConfiguration.ApplicationCertificates
                        .ToArray() ?? []).FirstOrDefault(id => id.CertificateType.IsNull);
                }
                if (instanceCertificate == null &&
                    certType == ObjectTypeIds.ApplicationCertificateType)
                {
                    instanceCertificate = (m_securityConfiguration.ApplicationCertificates
                        .ToArray() ?? []).FirstOrDefault();
                }
                if (instanceCertificate == null && certType == ObjectTypeIds.HttpsCertificateType)
                {
                    instanceCertificate = (m_securityConfiguration.ApplicationCertificates
                        .ToArray() ?? []).FirstOrDefault();
                }
                if (instanceCertificate != null)
                {
                    return instanceCertificate.Certificate;
                }
            }
            return null;
        }

        /// <summary>
        /// Loads the cached certificate chain blob of a certificate for use in a secure channel as raw byte array from cache.
        /// </summary>
        /// <param name="certificate">The application certificate.</param>
        public byte[]? LoadCertificateChainRaw(Certificate? certificate)
        {
            if (certificate == null)
            {
                return null;
            }

            if (m_certificateChain.TryGetValue(
                    certificate.Thumbprint,
                    out Tuple<CertificateCollection, byte[]>? result
                ) &&
                result.Item2 != null)
            {
                return result.Item2;
            }

            return certificate.RawData;
        }

        /// <summary>
        /// Loads the certificate chain for an application certificate.
        /// </summary>
        /// <param name="certificate">The application certificate.</param>
        public async Task<CertificateCollection?> LoadCertificateChainAsync(
            Certificate? certificate)
        {
            if (certificate == null)
            {
                return null;
            }

            if (m_certificateChain.TryGetValue(
                    certificate.Thumbprint,
                    out Tuple<CertificateCollection, byte[]>? certificateChainTuple))
            {
                // Return a new collection with AddRef'd members so the
                // caller can dispose independently without invalidating the cache.
                return CloneWithAddRef(certificateChainTuple.Item1);
            }

            // load certificate chain.
            var certificateChain = new CertificateCollection(new[] { certificate });
            var issuers = new List<Ua.CertificateIdentifier>();
            if (await m_certificateValidator.GetIssuersAsync(certificate, issuers)
                .ConfigureAwait(false))
            {
                for (int i = 0; i < issuers.Count; i++)
                {
                    Certificate? issuerCert = issuers[i].Certificate;
                    if (issuerCert != null)
                    {
                        certificateChain.Add(issuerCert);
                    }
                }
            }

            byte[] certificateChainRaw = Utils.CreateCertificateChainBlob(certificateChain);

            // AddRef the chain so the cache owns one set of references.
            certificateChain.AddRef();

            // update cached values
            m_certificateChain[certificate.Thumbprint]
                = new Tuple<CertificateCollection, byte[]>(
                certificateChain,
                certificateChainRaw);

            // Return a caller-owned copy so disposing it does not affect the cache.
            return CloneWithAddRef(certificateChain);
        }

        /// <summary>
        /// Loads the certificate chain for an application certificate from cache.
        /// </summary>
        /// <param name="certificate">The application certificate.</param>
        public CertificateCollection? LoadCertificateChain(Certificate? certificate)
        {
            if (certificate == null)
            {
                return null;
            }

            if (m_certificateChain.TryGetValue(
                    certificate.Thumbprint,
                    out Tuple<CertificateCollection, byte[]>? certificateChainTuple))
            {
                // Return a new collection with AddRef'd members so the
                // caller can dispose independently without invalidating the cache.
                return CloneWithAddRef(certificateChainTuple.Item1);
            }

            return null;
        }

        /// <summary>
        /// Update the security configuration of the cert type provider.
        /// </summary>
        /// <param name="securityConfiguration">The new security configuration.</param>
        public void Update(SecurityConfiguration securityConfiguration)
        {
            m_securityConfiguration = securityConfiguration;
            m_certificateChain.Clear();
            //ToDo intialize internal CertificateValidator after Certificate Update to clear cache of old application certificates
        }

        /// <summary>
        /// Creates a new <see cref="CertificateCollection"/> containing
        /// AddRef'd copies of the certificates in the source collection.
        /// The caller owns the returned collection and may dispose it
        /// independently of the source.
        /// </summary>
        private static CertificateCollection CloneWithAddRef(CertificateCollection source)
        {
            var result = new CertificateCollection(source.Count);
            foreach (Certificate cert in source)
            {
                result.Add(cert);
            }
            return result;
        }

        private readonly CertificateValidator m_certificateValidator;
        private SecurityConfiguration m_securityConfiguration;
        private readonly ConcurrentDictionary<string, Tuple<CertificateCollection, byte[]>> m_certificateChain;
    }
}
