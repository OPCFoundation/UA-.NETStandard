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
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
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
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Either none or both of HttpsIssuerCertificates & TrustedHttpsCertificates stores must be specified.");
            }

            if ((TrustedUserCertificates != null && UserIssuerCertificates == null) ||
                (UserIssuerCertificates != null && TrustedUserCertificates == null))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
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
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    storeName + " StorePath must be specified.");
            }
            try
            {
                ICertificateStore store =
                    storeIdentifier.OpenStore(telemetry)
                    ?? throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        $"Failed to open {storeName} store");
                store.Close();
            }
            catch (Exception ex)
            {
                ILogger<SecurityConfiguration> logger = telemetry.CreateLogger<SecurityConfiguration>();
                logger.LogError(ex, "Failed to open {StoreName} store", storeName);
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    storeName + " store is invalid.");
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
                            certId => certId.CertificateType == null);
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
                if (applicationCertificate.CertificateType == null)
                {
                    securityPolicies.Add(SecurityPolicies.Basic256Sha256);
                    securityPolicies.Add(SecurityPolicies.Aes128_Sha256_RsaOaep);
                    securityPolicies.Add(SecurityPolicies.Aes256_Sha256_RsaPss);
                    continue;
                }
                if (applicationCertificate.CertificateType.Identifier is uint identifier)
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
