/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
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
        #region Public Methods
        /// <summary>
        /// Adds a certificate as a trusted peer.
        /// </summary>
        public void AddTrustedPeer(byte[] certificate)
        {
            this.TrustedPeerCertificates.TrustedCertificates.Add(new CertificateIdentifier(certificate));
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

            TrustedIssuerCertificates = CreateDefaultTrustList(TrustedIssuerCertificates);
            TrustedPeerCertificates = CreateDefaultTrustList(TrustedPeerCertificates);

            //set a default rejected certificate store.
            if (RejectedCertificateStore == null)
            {
                RejectedCertificateStore = new CertificateStoreIdentifier();
                RejectedCertificateStore.StoreType = CertificateStoreType.Directory;
                RejectedCertificateStore.StorePath = Utils.DefaultLocalFolder + Path.DirectorySeparatorChar + "Rejected";
            }

            // replace subjectName DC=localhost with DC=hostname
            foreach (var applicationCertificate in m_applicationCertificates)
            {
                applicationCertificate.SubjectName = Utils.ReplaceDCLocalhost(applicationCertificate.SubjectName);
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
                        id = ApplicationCertificates.FirstOrDefault(certId => certId.CertificateType == null);
                    }
                    else if (certType == ObjectTypeIds.ApplicationCertificateType)
                    {
                        id = ApplicationCertificates.FirstOrDefault();
                    }
                    else if (certType == ObjectTypeIds.EccApplicationCertificateType)
                    {
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

        /// <summary>
        /// 
        /// </summary>
        private static Dictionary<uint, string> m_supportedCertificateTypes = new Dictionary<uint, string>() {
            { ObjectTypes.EccNistP256ApplicationCertificateType, "NistP256"},
            { ObjectTypes.EccNistP384ApplicationCertificateType, "NistP384"},
            { ObjectTypes.EccBrainpoolP256r1ApplicationCertificateType, "BrainpoolP256r1"},
            { ObjectTypes.EccBrainpoolP384r1ApplicationCertificateType, "BrainpoolP384r1"},
#if CURVE25519
            { ObjectTypes.EccCurve25519ApplicationCertificateType, "Curve25519"},
            { ObjectTypes.EccCurve448ApplicationCertificateType, "Curve448"},
#endif
            { ObjectTypes.RsaMinApplicationCertificateType, "RsaMin"},
            { ObjectTypes.RsaSha256ApplicationCertificateType, "RsaSha256"},
            { ObjectTypes.ApplicationCertificateType, "Rsa"},
            { ObjectTypes.HttpsCertificateType, "Https"},
        };

        /// <summary>
        /// Encode certificate types as comma seperated string.
        /// </summary>
        private string EncodeApplicationCertificateTypes()
        {
            if (m_applicationCertificates != null)
            {
                var result = new StringBuilder();
                bool commaRequired = false;
                foreach (var applicationCertificate in m_applicationCertificates)
                {
                    string idName = null;
                    if (applicationCertificate.CertificateType == null)
                    {
                        idName = "Rsa";
                    }
                    else if (applicationCertificate.CertificateType.Identifier is uint identifier)
                    {
                        if (m_supportedCertificateTypes.TryGetValue(identifier, out idName))
                        {
                            idName = idName.Substring(0, idName.IndexOf(nameof(ObjectTypes.ApplicationCertificateType), StringComparison.OrdinalIgnoreCase));
                        }
                    }

                    if (!result.ToString().Contains(idName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (commaRequired)
                        {
                            result.Append(',');
                        }
                        result.Append(idName);
                        commaRequired = true;
                    }
                }
                if (commaRequired)
                {
                    return result.ToString();
                }
            }
            return null;
        }

        /// <summary>
        /// Clones the default application certificate with
        /// certificate types specified in the configuration.
        /// </summary>
        /// <param name="certificateTypes">
        /// A comma seperated string of certificate
        /// types to clone from the default certificate.
        /// </param>
        private void DecodeApplicationCertificateTypes(string certificateTypes)
        {
            if (m_applicationCertificates.Count > 0)
            {
                CertificateIdentifier template = m_applicationCertificates[0];
                if (!String.IsNullOrWhiteSpace(certificateTypes))
                {
                    var result = new NodeIdDictionary<CertificateIdentifier>();
                    var certificateTypesArray = certificateTypes.Trim().Split(",", StringSplitOptions.RemoveEmptyEntries);
                    foreach (var certType in certificateTypesArray)
                    {
                        foreach (var profile in m_supportedCertificateTypes)
                        {
                            if (profile.Value.Contains(certType.Trim(), StringComparison.OrdinalIgnoreCase))
                            {
                                var certificateType = new NodeId(profile.Key);
                                m_applicationCertificates.Add(new CertificateIdentifier() {
                                    StoreType = template.StoreType,
                                    StorePath = template.StorePath,
                                    SubjectName = template.SubjectName,
                                    CertificateType = certificateType
                                });
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError, "Need application certificate to clone certificate types.");
            }
        }

        /// <summary>
        /// Get the provider which is invoked when a password
        /// for a private key is requested.
        /// </summary>
        public ICertificatePasswordProvider CertificatePasswordProvider { get; set; }
        #endregion
    }
    #endregion
}
