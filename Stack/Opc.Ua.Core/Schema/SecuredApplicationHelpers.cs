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
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Security
{
    /// <summary>
    /// Stores the security settings for an application.
    /// </summary>
    public partial class SecuredApplication
    {
        /// <summary>
        /// Casts a ApplicationType value.
        /// </summary>
        public static Ua.ApplicationType FromApplicationType(ApplicationType input)
        {
            return (Ua.ApplicationType)(int)input;
        }

        /// <summary>
        /// Casts a ApplicationType value.
        /// </summary>
        public static ApplicationType ToApplicationType(Ua.ApplicationType input)
        {
            return (ApplicationType)(int)input;
        }

        /// <summary>
        /// Creates a CertificateIdentifier object.
        /// </summary>
        public static CertificateIdentifier ToCertificateIdentifier(Ua.CertificateIdentifier input)
        {
            if (input != null &&
                !string.IsNullOrEmpty(input.StoreType) &&
                !string.IsNullOrEmpty(input.StorePath))
            {
                return new CertificateIdentifier
                {
                    StoreType = input.StoreType,
                    StorePath = input.StorePath,
                    SubjectName = input.SubjectName,
                    Thumbprint = input.Thumbprint,
                    ValidationOptions = (int)input.ValidationOptions,
                    OfflineRevocationList = null,
                    OnlineRevocationList = null
                };
            }

            return null;
        }

        /// <summary>
        /// Creates a CertificateIdentifier object.
        /// </summary>
        public static Ua.CertificateIdentifier FromCertificateIdentifier(
            CertificateIdentifier input)
        {
            var output = new Ua.CertificateIdentifier();

            if (input != null)
            {
                output.StoreType = input.StoreType;
                output.StorePath = input.StorePath;
                output.SubjectName = input.SubjectName;
                output.Thumbprint = input.Thumbprint;
                output.ValidationOptions = (CertificateValidationOptions)input.ValidationOptions;
            }

            return output;
        }

        /// <summary>
        /// Creates a CertificateStoreIdentifier object.
        /// </summary>
        public static CertificateStoreIdentifier ToCertificateStoreIdentifier(
            Ua.CertificateStoreIdentifier input)
        {
            if (input != null &&
                !string.IsNullOrEmpty(input.StoreType) &&
                !string.IsNullOrEmpty(input.StorePath))
            {
                return new CertificateStoreIdentifier
                {
                    StoreType = input.StoreType,
                    StorePath = input.StorePath,
                    ValidationOptions = (int)input.ValidationOptions
                };
            }

            return null;
        }

        /// <summary>
        /// Creates a CertificateTrustList object.
        /// </summary>
        public static CertificateTrustList FromCertificateStoreIdentifierToTrustList(
            CertificateStoreIdentifier input)
        {
            var output = new CertificateTrustList();

            if (input != null)
            {
                output.StoreType = input.StoreType;
                output.StorePath = input.StorePath;
                output.ValidationOptions = (CertificateValidationOptions)input.ValidationOptions;
            }

            return output;
        }

        /// <summary>
        /// Creates a CertificateStoreIdentifier object.
        /// </summary>
        public static Ua.CertificateStoreIdentifier FromCertificateStoreIdentifier(
            CertificateStoreIdentifier input)
        {
            var output = new Ua.CertificateStoreIdentifier();

            if (input != null)
            {
                output.StoreType = input.StoreType;
                output.StorePath = input.StorePath;
                output.ValidationOptions = (CertificateValidationOptions)input.ValidationOptions;
            }

            return output;
        }

        /// <summary>
        /// Creates a CertificateTrustList object.
        /// </summary>
        public static CertificateTrustList ToCertificateTrustList(CertificateStoreIdentifier input)
        {
            var output = new CertificateTrustList();

            if (input != null)
            {
                output.StoreType = input.StoreType;
                output.StorePath = input.StorePath;
                output.ValidationOptions = (CertificateValidationOptions)input.ValidationOptions;
            }

            return output;
        }

        /// <summary>
        /// Creates a CertificateList object.
        /// </summary>
        public static CertificateList ToCertificateList(CertificateIdentifierCollection input)
        {
            var output = new CertificateList();

            if (input != null)
            {
                output.ValidationOptions = 0;
                output.Certificates = [];

                for (int ii = 0; ii < input.Count; ii++)
                {
                    output.Certificates.Add(ToCertificateIdentifier(input[ii]));
                }
            }

            return output;
        }

        /// <summary>
        /// Creates a CertificateIdentifierCollection object.
        /// </summary>
        public static CertificateIdentifierCollection FromCertificateList(CertificateList input)
        {
            var output = new CertificateIdentifierCollection();

            if (input != null && input.Certificates != null)
            {
                for (int ii = 0; ii < input.Certificates.Count; ii++)
                {
                    output.Add(FromCertificateIdentifier(input.Certificates[ii]));
                }
            }

            return output;
        }

        /// <summary>
        /// Creates a ListOfBaseAddresses object.
        /// </summary>
        public static ListOfBaseAddresses ToListOfBaseAddresses(
            ServerBaseConfiguration configuration)
        {
            var addresses = new ListOfBaseAddresses();

            if (configuration != null)
            {
                if (configuration.BaseAddresses != null)
                {
                    for (int ii = 0; ii < configuration.BaseAddresses.Count; ii++)
                    {
                        addresses.Add(configuration.BaseAddresses[ii]);
                    }
                }

                if (configuration.AlternateBaseAddresses != null)
                {
                    for (int ii = 0; ii < configuration.AlternateBaseAddresses.Count; ii++)
                    {
                        addresses.Add(configuration.AlternateBaseAddresses[ii]);
                    }
                }
            }

            return addresses;
        }

        /// <summary>
        /// Creates a ListOfBaseAddresses object.
        /// </summary>
        public static void FromListOfBaseAddresses(
            ServerBaseConfiguration configuration,
            ListOfBaseAddresses addresses)
        {
            var map = new Dictionary<string, string>();

            if (addresses != null && configuration != null)
            {
                configuration.BaseAddresses = [];
                configuration.AlternateBaseAddresses = null;

                for (int ii = 0; ii < addresses.Count; ii++)
                {
                    Uri url = Utils.ParseUri(addresses[ii]);

                    if (url != null)
                    {
                        if (!map.TryAdd(url.Scheme, string.Empty))
                        {
                            (configuration.AlternateBaseAddresses ??= []).Add(url.ToString());
                        }
                        else
                        {
                            configuration.BaseAddresses.Add(url.ToString());
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Creates a ListOfSecurityProfiles object.
        /// </summary>
        public static ListOfSecurityProfiles ToListOfSecurityProfiles(
            ServerSecurityPolicyCollection policies)
        {
            var profiles = new ListOfSecurityProfiles
            {
                CreateProfile(SecurityPolicies.None),
                CreateProfile(SecurityPolicies.Basic128Rsa15),
                CreateProfile(SecurityPolicies.Basic256),
                CreateProfile(SecurityPolicies.Basic256Sha256),
                CreateProfile(SecurityPolicies.Aes128_Sha256_RsaOaep),
                CreateProfile(SecurityPolicies.Aes256_Sha256_RsaPss)
            };

            if (policies != null)
            {
                for (int ii = 0; ii < policies.Count; ii++)
                {
                    for (int jj = 0; jj < profiles.Count; jj++)
                    {
                        if (policies[ii].SecurityPolicyUri == profiles[jj].ProfileUri)
                        {
                            profiles[jj].Enabled = true;
                        }
                    }
                }
            }

            return profiles;
        }

        /// <summary>
        /// Creates a ServerSecurityPolicyCollection object.
        /// </summary>
        public static ServerSecurityPolicyCollection FromListOfSecurityProfiles(
            ListOfSecurityProfiles profiles)
        {
            var policies = new ServerSecurityPolicyCollection();

            if (profiles != null)
            {
                for (int ii = 0; ii < profiles.Count; ii++)
                {
                    if (profiles[ii].Enabled)
                    {
                        policies.Add(CreatePolicy(profiles[ii].ProfileUri));
                    }
                }
            }

            if (policies.Count == 0)
            {
                policies.Add(CreatePolicy(SecurityPolicies.None));
            }

            return policies;
        }

        /// <summary>
        /// Obsolete version of CalculateSecurityLevel that does not take a logger.
        /// </summary>
        [Obsolete("Use CalculateSecurityLevel(MessageSecurityMode mode, string policyUri, ILogger logger) instead.")]
        public static byte CalculateSecurityLevel(
            MessageSecurityMode mode,
            string policyUri)
        {
            ILogger logger = AmbientMessageContext.Telemetry.CreateLogger<SecuredApplication>();
            return CalculateSecurityLevel(mode, policyUri, logger);
        }

        /// <summary>
        /// Calculates the security level, given the security mode and policy
        /// Invalid and none is discouraged
        /// Just signing is always weaker than any use of encryption
        /// </summary>
        public static byte CalculateSecurityLevel(
            MessageSecurityMode mode,
            string policyUri,
            ILogger logger)
        {
            if ((mode != MessageSecurityMode.Sign && mode != MessageSecurityMode.SignAndEncrypt) ||
                policyUri == null)
            {
                return 0;
            }

            byte result;
            switch (policyUri)
            {
                case SecurityPolicies.Basic128Rsa15:
                    logger.LogWarning(
                        "Deprecated Security Policy Basic128Rsa15 requested - Not recommended.");
                    result = 2;
                    break;
                case SecurityPolicies.Basic256:
                    logger.LogWarning("Deprecated Security Policy Basic256 requested - Not recommended.");
                    result = 4;
                    break;
                case SecurityPolicies.ECC_nistP256:
                case SecurityPolicies.ECC_nistP384:
                    logger.LogWarning("Deprecated Security Policy {PolicyUri} requested - Use ECC_nistP[256/384]_AES.", policyUri);
                    result = 4;
                    break;
                case SecurityPolicies.ECC_brainpoolP256r1:
                case SecurityPolicies.ECC_brainpoolP384r1:
                    logger.LogWarning("Deprecated Security Policy {PolicyUri} requested - Use ECC_brainpoolP[256/384]r1_AES.", policyUri);
                    result = 4;
                    break;
                case SecurityPolicies.Basic256Sha256:
                    result = 6;
                    break;
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                    result = 8;
                    break;
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                    result = 10;
                    break;
                case SecurityPolicies.ECC_brainpoolP256r1_AesGcm:
                case SecurityPolicies.ECC_brainpoolP256r1_ChaChaPoly:
                case SecurityPolicies.ECC_nistP256_AesGcm:
                case SecurityPolicies.ECC_nistP256_ChaChaPoly:
                    result = 12;
                    break;
                case SecurityPolicies.ECC_nistP384_AesGcm:
                case SecurityPolicies.ECC_nistP384_ChaChaPoly:
                case SecurityPolicies.ECC_brainpoolP384r1_AesGcm:
                case SecurityPolicies.ECC_brainpoolP384r1_ChaChaPoly:
                    result = 14;
                    break;
                case SecurityPolicies.None:
                    return 0;
                default:
                    logger.LogWarning(
                        "Security level requested for unknown Security Policy {Policy}. Returning security level 0",
                        policyUri);
                    return 0;
            }

            if (mode == MessageSecurityMode.SignAndEncrypt)
            {
                result += 100;
            }

            return result;
        }

        /// <summary>
        /// Creates a new policy object.
        /// Always uses sign and encrypt for all security policies except none
        /// </summary>
        private static ServerSecurityPolicy CreatePolicy(string profileUri)
        {
            return new ServerSecurityPolicy
            {
                SecurityPolicyUri = profileUri,
                SecurityMode = profileUri == SecurityPolicies.None ?
                    MessageSecurityMode.None :
                    MessageSecurityMode.SignAndEncrypt
            };
        }

        /// <summary>
        /// Creates a new policy object.
        /// </summary>
        private static SecurityProfile CreateProfile(string profileUri)
        {
            return new SecurityProfile { ProfileUri = profileUri, Enabled = false };
        }

        /// <summary>
        ///  TODO: Holds the application certificates but should be generated and the Opc.Ua.Security namespace automatically
        ///  TODO: Should replace ApplicationCertificateField in the generated Opc.Ua.Security.SecuredApplication class
        /// </summary>
        public CertificateList ApplicationCertificates { get; set; }
    }

    /// <summary>
    /// An identifier for a certificate.
    /// </summary>
    public partial class CertificateIdentifier
    {
        /// <summary>
        /// Gets the certificate associated with the identifier.
        /// </summary>
        [Obsolete("Use FindAsync()")]
        public Task<X509Certificate2> Find()
        {
            return FindAsync(null);
        }

        /// <summary>
        /// Gets the certificate associated with the identifier.
        /// </summary>
        public Task<X509Certificate2> FindAsync(
            ITelemetryContext telemetry,
            CancellationToken ct = default)
        {
            Ua.CertificateIdentifier output = SecuredApplication.FromCertificateIdentifier(this);
            return output.FindAsync(false, telemetry: telemetry, ct: ct);
        }

        /// <summary>
        /// Gets the certificate associated with the identifier.
        /// </summary>
        [Obsolete("Use FindAsync(needPrivateKey)")]
        public Task<X509Certificate2> Find(bool needPrivateKey)
        {
            return FindAsync(needPrivateKey, null);
        }

        /// <summary>
        /// Gets the certificate associated with the identifier.
        /// </summary>
        public Task<X509Certificate2> FindAsync(
            bool needPrivateKey,
            ITelemetryContext telemetry,
            CancellationToken ct = default)
        {
            Ua.CertificateIdentifier output = SecuredApplication.FromCertificateIdentifier(this);
            return output.FindAsync(needPrivateKey, telemetry: telemetry, ct: ct);
        }

        /// <summary>
        /// Opens the certificate store.
        /// </summary>
        public ICertificateStore OpenStore(ITelemetryContext telemetry)
        {
            Ua.CertificateIdentifier output = SecuredApplication.FromCertificateIdentifier(this);
            return output.OpenStore(telemetry);
        }
    }

    /// <summary>
    /// An identifier for a certificate store.
    /// </summary>
    public partial class CertificateStoreIdentifier
    {
        /// <summary>
        /// Opens the certificate store.
        /// </summary>
        public ICertificateStore OpenStore(ITelemetryContext telemetry)
        {
            Ua.CertificateStoreIdentifier output = SecuredApplication
                .FromCertificateStoreIdentifier(this);
            return output.OpenStore(telemetry);
        }
    }
}
