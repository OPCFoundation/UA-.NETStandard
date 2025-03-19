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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Opc.Ua
{
    #region SecurityConfiguration Class
    /// <summary>
    /// The security configuration for the application.
    /// </summary>
    public partial class SecurityConfiguration
    {
        #region Public Properties
        /// <summary>
        /// The security profiles which are supported for this configuration.
        /// </summary>
        public StringCollection SupportedSecurityPolicies { get; private set; }

        /// <summary>
        /// Get the provider which is invoked when a password
        /// for a private key is requested.
        /// </summary>
        public ICertificatePasswordProvider CertificatePasswordProvider { get; set; }
        #endregion


        #region Public Methods
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
        public void Validate()
        {
            if (m_applicationCertificates == null ||
                m_applicationCertificates.Count == 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "ApplicationCertificate must be specified.");
            }
            // ensure mandatory stores are valid
            ValidateStore(TrustedIssuerCertificates, nameof(TrustedIssuerCertificates));
            ValidateStore(TrustedPeerCertificates, nameof(TrustedPeerCertificates));

            ///ensure optional stores are valid if specified
            if (TrustedHttpsCertificates.StorePath != null)
            {
                ValidateStore(TrustedHttpsCertificates, nameof(TrustedHttpsCertificates));
            }
            if (HttpsIssuerCertificates.StorePath != null)
            {
                ValidateStore(HttpsIssuerCertificates, nameof(HttpsIssuerCertificates));
            }
            if (TrustedUserCertificates.StorePath != null)
            {
                ValidateStore(TrustedUserCertificates, nameof(TrustedUserCertificates));
            }
            if (UserIssuerCertificates.StorePath != null)
            {
                ValidateStore(UserIssuerCertificates, nameof(UserIssuerCertificates));
            }


            if ((TrustedHttpsCertificates.StorePath != null && HttpsIssuerCertificates.StorePath == null)
                || (HttpsIssuerCertificates.StorePath != null && TrustedHttpsCertificates.StorePath == null))
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "Either none or both of HttpsIssuerCertificates & TrustedHttpsCertificates stores must be specified.");
            }

            if ((TrustedUserCertificates.StorePath != null && UserIssuerCertificates.StorePath == null)
                || (UserIssuerCertificates.StorePath != null && TrustedUserCertificates.StorePath == null))
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "Either none or both of UserIssuerCertificates & TrustedUserCertificates stores must be specified.");
            }


            // replace subjectName DC=localhost with DC=hostname
            foreach (var applicationCertificate in m_applicationCertificates)
            {
                applicationCertificate.SubjectName = Utils.ReplaceDCLocalhost(applicationCertificate.SubjectName);
            }
        }
        /// <summary>
        /// Validate if the specified store can be opened
        /// throws ServiceResultException
        /// </summary>
        private void ValidateStore(CertificateStoreIdentifier storeIdentifier, string storeName)
        {
            if (storeIdentifier?.StorePath == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError, storeName + " store must be specified.");
            }
            try
            {
                ICertificateStore store = storeIdentifier.OpenStore();
                if (store == null)
                {
                    throw new Exception($"Failed top open {storeName} store");
                }
                store?.Close();
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Failed top open {storeName} store", storeName);
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError, storeName + " store is invalid.");
            }
        }

        /// <summary>
        /// Find application certificate for a security policy.
        /// </summary>
        /// <param name="securityPolicy"></param>
        /// <param name="privateKey"></param>
        public async Task<X509Certificate2> FindApplicationCertificateAsync(string securityPolicy, bool privateKey)
        {
            var certificateTypes = CertificateIdentifier.MapSecurityPolicyToCertificateTypes(securityPolicy);
            foreach (var certType in certificateTypes)
            {
                CertificateIdentifier id = ApplicationCertificates.FirstOrDefault(certId => certId.CertificateType == certType);
                if (id == null)
                {
                    if (certType == ObjectTypeIds.RsaSha256ApplicationCertificateType)
                    {
                        // undefined certificate type as RsaSha256
                        id = ApplicationCertificates.FirstOrDefault(certId => certId.CertificateType == null);
                    }
                    else if (certType == ObjectTypeIds.ApplicationCertificateType)
                    {
                        // first certificate
                        id = ApplicationCertificates.FirstOrDefault();
                    }
                    else if (certType == ObjectTypeIds.EccApplicationCertificateType)
                    {
                        // first Ecc certificate
                        id = ApplicationCertificates.FirstOrDefault(certId => X509Utils.IsECDsaSignature(certId.Certificate));
                    }
                }

                if (id != null)
                {
                    return await id.Find(privateKey).ConfigureAwait(false);
                }
            }

            return null;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Use the list of application certificates to build a list
        /// of supported security policies.
        /// </summary>
        private StringCollection BuildSupportedSecurityPolicies()
        {
            var securityPolicies = new StringCollection();
            securityPolicies.Add(SecurityPolicies.None);
            foreach (var applicationCertificate in m_applicationCertificates)
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
            foreach (var securityPolicyUri in securityPolicies.Distinct())
            {
                if (SecurityPolicies.GetDisplayName(securityPolicyUri) != null)
                {
                    result.Add(securityPolicyUri);
                }
            }
            return result;
        }

        /// <summary>
        /// Ensure valid trust lists.
        /// </summary>
        private CertificateTrustList CreateDefaultTrustList(CertificateTrustList trustList)
        {
            if (trustList != null)
            {
                if (trustList.StorePath != null)
                {
                    return trustList;
                }
            }

            return new CertificateTrustList();
        }




        #endregion
    }
    #endregion
}
