/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Opc.Ua;

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
        private CertificateTypesProvider() { }

        /// <summary>
        /// Create an instance of the certificate provider.
        /// </summary>
        public CertificateTypesProvider(ApplicationConfiguration config)
        {
            m_securityConfiguration = config.SecurityConfiguration;
            m_certificateValidator = new CertificateValidator();
            m_certificateChain = new ConcurrentDictionary<string, Tuple<X509Certificate2Collection, byte[]>>();
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
        public X509Certificate2 GetInstanceCertificate(string securityPolicyUri)
        {
            if (securityPolicyUri == SecurityPolicies.None)
            {
                // return the default certificate for None
                return m_securityConfiguration.ApplicationCertificates.FirstOrDefault().Certificate;
            }
            var certificateTypes = Opc.Ua.CertificateIdentifier.MapSecurityPolicyToCertificateTypes(securityPolicyUri);
            foreach (var certType in certificateTypes)
            {
                var instanceCertificate = m_securityConfiguration.ApplicationCertificates.FirstOrDefault(id => id.CertificateType == certType);
                if (instanceCertificate == null &&
                    certType == ObjectTypeIds.RsaSha256ApplicationCertificateType)
                {
                    instanceCertificate = m_securityConfiguration.ApplicationCertificates.FirstOrDefault(id => id.CertificateType == null);
                }
                if (instanceCertificate == null &&
                    certType == ObjectTypeIds.ApplicationCertificateType)
                {
                    instanceCertificate = m_securityConfiguration.ApplicationCertificates.FirstOrDefault();
                }
                if (instanceCertificate == null &&
                    certType == ObjectTypeIds.HttpsCertificateType)
                {
                    instanceCertificate = m_securityConfiguration.ApplicationCertificates.FirstOrDefault();
                }
                if (instanceCertificate != null)
                {
                    return instanceCertificate.Certificate;
                }
            }
            return null;
        }

        /// <summary>
        /// Loads the cached certificate chain blob of a certificate for use in a secure channel as raw byte array.
        /// </summary>
        /// <param name="certificate">The application certificate.</param>
        public async Task<byte[]> LoadCertificateChainRawAsync(X509Certificate2 certificate)
        {
            if (certificate == null)
            {
                return null;
            }

            if (m_certificateChain.TryGetValue(certificate.Thumbprint, out var result) && result.Item2 != null)
            {
                return result.Item2;
            }

            // load certificate chain.
            Tuple<X509Certificate2Collection, byte[]> dictionaryValue = await LoadCertificateChainFromStoreAsync(certificate).ConfigureAwait(false);

            // update cached values
            m_certificateChain[certificate.Thumbprint] = dictionaryValue;

            return dictionaryValue.Item2;
        }

        /// <summary>
        /// Loads the certificate chain for an application certificate.
        /// </summary>
        /// <param name="certificate">The application certificate.</param>
        public async Task<X509Certificate2Collection> LoadCertificateChainAsync(X509Certificate2 certificate)
        {
            if (certificate == null)
            {
                return null;
            }

            if (m_certificateChain.TryGetValue(certificate.Thumbprint, out var certificateChainTuple))
            {
                return certificateChainTuple.Item1;
            }

            // load certificate chain.
            Tuple<X509Certificate2Collection, byte[]> dictionaryValue = await LoadCertificateChainFromStoreAsync(certificate).ConfigureAwait(false);

            // update cached values
            m_certificateChain[certificate.Thumbprint] = dictionaryValue;

            return dictionaryValue.Item1;
        }

        /// <summary>
        /// Update the security configuration of the cert type provider.
        /// </summary>
        /// <param name="securityConfiguration">The new security configuration.</param>
        public async Task UpdateAsync(SecurityConfiguration securityConfiguration)
        {
            m_securityConfiguration = securityConfiguration;
            //ToDo intialize internal CertificateValidator after Certificate Update to clear cache of old application certificates
            await Task.CompletedTask.ConfigureAwait(false);
        }

        /// <summary>
        /// Builds the chain using the Issuer and Trusted Stores of the certificateValidator
        /// </summary>
        /// <param name="certificate">the certificate to load the chain for</param>
        /// <returns></returns>
        private async Task<Tuple<X509Certificate2Collection, byte[]>> LoadCertificateChainFromStoreAsync(X509Certificate2 certificate)
        {
            var certificateChain = new X509Certificate2Collection(certificate);
            var issuers = new List<Opc.Ua.CertificateIdentifier>();
            if (await m_certificateValidator.GetIssuers(certificate, issuers).ConfigureAwait(false))
            {
                for (int i = 0; i < issuers.Count; i++)
                {
                    certificateChain.Add(issuers[i].Certificate);
                }
            }

            byte[] certificateChainRaw = Utils.CreateCertificateChainBlob(certificateChain);
            return new Tuple<X509Certificate2Collection, byte[]>(certificateChain, certificateChainRaw);
        }

        CertificateValidator m_certificateValidator;
        SecurityConfiguration m_securityConfiguration;
        ConcurrentDictionary<string, Tuple<X509Certificate2Collection, byte[]>> m_certificateChain;
    }
}
