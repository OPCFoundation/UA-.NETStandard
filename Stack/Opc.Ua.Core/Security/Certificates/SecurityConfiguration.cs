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
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// The security configuration for the application.
    /// </summary>
    public partial class SecurityConfiguration
    {
        /// <summary>
        /// The security profiles which are supported for this configuration.
        /// </summary>
        public StringCollection SupportedSecurityPolicies { get; private set; }

        /// <summary>
        /// Get the provider which is invoked when a password
        /// for a private key is requested.
        /// </summary>
        public ICertificatePasswordProvider CertificatePasswordProvider { get; set; }

        /// <summary>
        /// Adds a certificate as a trusted peer.
        /// </summary>
        public void AddTrustedPeer(byte[] certificate)
        {
            TrustedPeerCertificates.TrustedCertificates.Add(new CertificateIdentifier(certificate));
        }

        /// <summary>
        /// Validates the security configuration.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public void Validate(ITelemetryContext telemetry)
        {
            if (m_applicationCertificates == null || m_applicationCertificates.Count == 0)
            {
                throw ServiceResultException.ConfigurationError(
                    "ApplicationCertificate must be specified.");
            }
            // ensure mandatory stores are valid
            ValidateStore(TrustedIssuerCertificates, nameof(TrustedIssuerCertificates), telemetry);
            ValidateStore(TrustedPeerCertificates, nameof(TrustedPeerCertificates), telemetry);

            //ensure optional stores are valid if specified
            if (TrustedHttpsCertificates != null)
            {
                ValidateStore(TrustedHttpsCertificates, nameof(TrustedHttpsCertificates), telemetry);
            }
            if (HttpsIssuerCertificates != null)
            {
                ValidateStore(HttpsIssuerCertificates, nameof(HttpsIssuerCertificates), telemetry);
            }
            if (TrustedUserCertificates != null)
            {
                ValidateStore(TrustedUserCertificates, nameof(TrustedUserCertificates), telemetry);
            }
            if (UserIssuerCertificates != null)
            {
                ValidateStore(UserIssuerCertificates, nameof(UserIssuerCertificates), telemetry);
            }

            if ((TrustedHttpsCertificates != null && HttpsIssuerCertificates == null) ||
                (HttpsIssuerCertificates != null && TrustedHttpsCertificates == null))
            {
                throw ServiceResultException.ConfigurationError(
                    "Either none or both of HttpsIssuerCertificates & TrustedHttpsCertificates stores must be specified.");
            }

            if ((TrustedUserCertificates != null && UserIssuerCertificates == null) ||
                (UserIssuerCertificates != null && TrustedUserCertificates == null))
            {
                throw ServiceResultException.ConfigurationError(
                    "Either none or both of UserIssuerCertificates & TrustedUserCertificates stores must be specified.");
            }

            // replace subjectName DC=localhost with DC=hostname
            foreach (CertificateIdentifier applicationCertificate in m_applicationCertificates)
            {
                applicationCertificate.SubjectName = Utils.ReplaceDCLocalhost(
                    applicationCertificate.SubjectName);
            }
        }

        /// <summary>
        /// Validate if the specified store can be opened
        /// throws ServiceResultException
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void ValidateStore(
            CertificateTrustList storeIdentifier,
            string storeName,
            ITelemetryContext telemetry)
        {
            if (string.IsNullOrEmpty(storeIdentifier?.StorePath))
            {
                throw ServiceResultException.ConfigurationError(
                    "{0} StorePath must be specified.", storeName);
            }
            try
            {
                ICertificateStore store = storeIdentifier.OpenStore(telemetry) ??
                    throw ServiceResultException.ConfigurationError(
                        "Failed to open {0} store", storeName);
                store.Close();
            }
            catch (Exception ex)
            {
                ILogger<SecurityConfiguration> logger = telemetry.CreateLogger<SecurityConfiguration>();
                logger.LogError(ex, "Failed to open {StoreName} store", storeName);
                throw ServiceResultException.ConfigurationError("{0} store is invalid.", storeName);
            }
        }

        /// <summary>
        /// Find application certificate for a security policy.
        /// </summary>
        public async Task<X509Certificate2> FindApplicationCertificateAsync(
            string securityPolicy,
            bool privateKey,
            ITelemetryContext telemetry,
            CancellationToken ct = default)
        {
            foreach (NodeId certType in CertificateIdentifier.MapSecurityPolicyToCertificateTypes(
                securityPolicy))
            {
                CertificateIdentifier id = ApplicationCertificates.FirstOrDefault(certId =>
                    certId.CertificateType == certType);
                if (id == null)
                {
                    if (certType == ObjectTypeIds.RsaSha256ApplicationCertificateType)
                    {
                        // undefined certificate type as RsaSha256
                        id = ApplicationCertificates.FirstOrDefault(
                            certId => certId.CertificateType.IsNull);
                    }
                    else if (certType == ObjectTypeIds.ApplicationCertificateType)
                    {
                        // first certificate
                        id = ApplicationCertificates.FirstOrDefault();
                    }
                    else if (certType == ObjectTypeIds.EccApplicationCertificateType)
                    {
                        // first Ecc certificate
                        id = ApplicationCertificates.FirstOrDefault(certId =>
                            X509Utils.IsECDsaSignature(certId.Certificate));
                    }
                }

                if (id != null)
                {
                    return await id.FindAsync(
                        privateKey,
                        applicationUri: null,
                        telemetry: telemetry,
                        ct).ConfigureAwait(false);
                }
            }

            return null;
        }

        /// <summary>
        /// Use the list of application certificates to build a list
        /// of supported security policies.
        /// </summary>
        private StringCollection BuildSupportedSecurityPolicies()
        {
            var securityPolicies = new StringCollection { SecurityPolicies.None };
            foreach (CertificateIdentifier applicationCertificate in m_applicationCertificates)
            {
                if (applicationCertificate.CertificateType.IsNull)
                {
                    securityPolicies.Add(SecurityPolicies.Basic256Sha256);
                    securityPolicies.Add(SecurityPolicies.Aes128_Sha256_RsaOaep);
                    securityPolicies.Add(SecurityPolicies.Aes256_Sha256_RsaPss);
                    continue;
                }
                if (applicationCertificate.CertificateType.TryGetIdentifier(out uint identifier))
                {
                    switch (identifier)
                    {
                        case ObjectTypes.EccNistP256ApplicationCertificateType:
                            securityPolicies.Add(SecurityPolicies.ECC_nistP256);
                            break;
                        case ObjectTypes.EccNistP384ApplicationCertificateType:
                            securityPolicies.Add(SecurityPolicies.ECC_nistP256);
                            securityPolicies.Add(SecurityPolicies.ECC_nistP384);
                            break;
                        case ObjectTypes.EccBrainpoolP256r1ApplicationCertificateType:
                            securityPolicies.Add(SecurityPolicies.ECC_brainpoolP256r1);
                            break;
                        case ObjectTypes.EccBrainpoolP384r1ApplicationCertificateType:
                            securityPolicies.Add(SecurityPolicies.ECC_brainpoolP256r1);
                            securityPolicies.Add(SecurityPolicies.ECC_brainpoolP384r1);
                            break;
                        case ObjectTypes.EccCurve25519ApplicationCertificateType:
                            securityPolicies.Add(SecurityPolicies.ECC_curve25519);
                            break;
                        case ObjectTypes.EccCurve448ApplicationCertificateType:
                            securityPolicies.Add(SecurityPolicies.ECC_curve448);
                            break;
                        case ObjectTypes.RsaMinApplicationCertificateType:
                            securityPolicies.Add(SecurityPolicies.Basic128Rsa15);
                            securityPolicies.Add(SecurityPolicies.Basic256);
                            break;
                        case ObjectTypes.ApplicationCertificateType:
                        case ObjectTypes.RsaSha256ApplicationCertificateType:
                            securityPolicies.Add(SecurityPolicies.Basic256Sha256);
                            securityPolicies.Add(SecurityPolicies.Aes128_Sha256_RsaOaep);
                            securityPolicies.Add(SecurityPolicies.Aes256_Sha256_RsaPss);
                            goto case ObjectTypes.RsaMinApplicationCertificateType;
                    }
                }
            }
            // filter based on platform support
            var result = new StringCollection();
            foreach (string securityPolicyUri in securityPolicies.Distinct())
            {
                if (SecurityPolicies.GetDisplayName(securityPolicyUri) != null)
                {
                    result.Add(securityPolicyUri);
                }
            }
            return result;
        }
    }
}
