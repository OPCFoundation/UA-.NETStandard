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
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Opc.Ua.Security
{
    /// <summary>
    /// Stores the security settings for an application.
    /// </summary>
    public partial class SecuredApplication
    {
        /// <summary>
        /// The name of the application.
        /// </summary>
        [Obsolete("Replaced by ApplicationName")]
        public string Name
        {
            get { return this.ApplicationName; }
            set { this.ApplicationName = value; }
        }

        /// <summary>
        /// The uri of the application.
        /// </summary>
        [Obsolete("Replaced by ApplicationUri")]
        public string Uri
        {
            get { return this.ApplicationUri; }
            set { this.ApplicationUri = value; }
        }

        /// <summary>
        /// A store of certificates trusted by the application.
        /// </summary>
        [Obsolete("Replaced by TrustedCertificateStore")]
        public CertificateStoreIdentifier TrustedPeerStore
        {
            get { return this.TrustedCertificateStore; }
            set { this.TrustedCertificateStore = value; }
        }

        /// <summary>
        /// A list of certificates trusted by the application.
        /// </summary>
        [Obsolete("Replaced by TrustedCertificates")]
        public CertificateList TrustedPeerCertificates
        {
            get { return this.TrustedCertificates; }
            set { this.TrustedCertificates = value; }
        }

        /// <summary>
        /// A store of certificate issuers used by the application.
        /// </summary>
        [Obsolete("Replaced by TrustedIssuerStore")]
        public CertificateStoreIdentifier TrustedIssuerStore
        {
            get { return this.IssuerCertificateStore; }
            set { this.IssuerCertificateStore = value; }
        }

        /// <summary>
        /// A list of certificate issuers used by the application.
        /// </summary>
        [Obsolete("Replaced by IssuerCertificates")]
        public CertificateList TrustedIssuerCertificates
        {
            get { return this.IssuerCertificates; }
            set { this.IssuerCertificates = value; }
        }

        /// <summary>
        /// Casts a ApplicationType value. 
        /// </summary>
        public static Opc.Ua.ApplicationType FromApplicationType(Opc.Ua.Security.ApplicationType input)
        {
            return (Opc.Ua.ApplicationType)(int)input;
        }

        /// <summary>
        /// Casts a ApplicationType value. 
        /// </summary>
        public static Opc.Ua.Security.ApplicationType ToApplicationType(Opc.Ua.ApplicationType input)
        {
            return (Opc.Ua.Security.ApplicationType)(int)input;
        }

        /// <summary>
        /// Creates a CertificateIdentifier object. 
        /// </summary>
        public static CertificateIdentifier ToCertificateIdentifier(Opc.Ua.CertificateIdentifier input)
        {
            if (input != null && !String.IsNullOrEmpty(input.StoreType) && !String.IsNullOrEmpty(input.StorePath))
            {
                CertificateIdentifier output = new CertificateIdentifier();

                output.StoreType = input.StoreType;
                output.StorePath = input.StorePath;
                output.SubjectName = input.SubjectName;
                output.Thumbprint = input.Thumbprint;
                output.ValidationOptions = (int)input.ValidationOptions;
                output.OfflineRevocationList = null;
                output.OnlineRevocationList = null;

                return output;
            }

            return null;
        }

        /// <summary>
        /// Creates a CertificateIdentifier object. 
        /// </summary>
        public static Opc.Ua.CertificateIdentifier FromCertificateIdentifier(CertificateIdentifier input)
        {
            Opc.Ua.CertificateIdentifier output = new Opc.Ua.CertificateIdentifier();

            if (input != null)
            {
                output.StoreType = input.StoreType;
                output.StorePath = input.StorePath;
                output.SubjectName = input.SubjectName;
                output.Thumbprint = input.Thumbprint;
                output.ValidationOptions = (Opc.Ua.CertificateValidationOptions)input.ValidationOptions;
            }

            return output;
        }

        /// <summary>
        /// Creates a CertificateStoreIdentifier object. 
        /// </summary>
        public static CertificateStoreIdentifier ToCertificateStoreIdentifier(Opc.Ua.CertificateStoreIdentifier input)
        {
            if (input != null && !String.IsNullOrEmpty(input.StoreType) && !String.IsNullOrEmpty(input.StorePath))
            {
                CertificateStoreIdentifier output = new CertificateStoreIdentifier();

                output.StoreType = input.StoreType;
                output.StorePath = input.StorePath;
                output.ValidationOptions = (int)input.ValidationOptions;

                return output;
            }

            return null;
        }

        /// <summary>
        /// Creates a CertificateTrustList object. 
        /// </summary>
        public static Opc.Ua.CertificateTrustList FromCertificateStoreIdentifierToTrustList(CertificateStoreIdentifier input)
        {
            Opc.Ua.CertificateTrustList output = new Opc.Ua.CertificateTrustList();

            if (input != null)
            {
                output.StoreType = input.StoreType;
                output.StorePath = input.StorePath;
                output.ValidationOptions = (Opc.Ua.CertificateValidationOptions)input.ValidationOptions;
            }

            return output;
        }

        /// <summary>
        /// Creates a CertificateStoreIdentifier object. 
        /// </summary>
        public static Opc.Ua.CertificateStoreIdentifier FromCertificateStoreIdentifier(CertificateStoreIdentifier input)
        {
            Opc.Ua.CertificateStoreIdentifier output = new Opc.Ua.CertificateStoreIdentifier();

            if (input != null)
            {
                output.StoreType = input.StoreType;
                output.StorePath = input.StorePath;
                output.ValidationOptions = (Opc.Ua.CertificateValidationOptions)input.ValidationOptions;
            }

            return output;
        }

        /// <summary>
        /// Creates a CertificateTrustList object. 
        /// </summary>
        public static Opc.Ua.CertificateTrustList ToCertificateTrustList(CertificateStoreIdentifier input)
        {
            Opc.Ua.CertificateTrustList output = new Opc.Ua.CertificateTrustList();

            if (input != null)
            {
                output.StoreType = input.StoreType;
                output.StorePath = input.StorePath;
                output.ValidationOptions = (Opc.Ua.CertificateValidationOptions)input.ValidationOptions;
            }

            return output;
        }

        /// <summary>
        /// Creates a CertificateList object. 
        /// </summary>
        public static CertificateList ToCertificateList(Opc.Ua.CertificateIdentifierCollection input)
        {
            CertificateList output = new CertificateList();

            if (input != null)
            {
                output.ValidationOptions = (int)0;
                output.Certificates = new ListOfCertificateIdentifier();

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
        public static Opc.Ua.CertificateIdentifierCollection FromCertificateList(CertificateList input)
        {
            Opc.Ua.CertificateIdentifierCollection output = new Opc.Ua.CertificateIdentifierCollection();

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
        public static ListOfBaseAddresses ToListOfBaseAddresses(ServerBaseConfiguration configuration)
        {
            ListOfBaseAddresses addresses = new ListOfBaseAddresses();

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
        public static void FromListOfBaseAddresses(ServerBaseConfiguration configuration, ListOfBaseAddresses addresses)
        {
            Dictionary<string, string> map = new Dictionary<string, string>();

            if (addresses != null && configuration != null)
            {
                configuration.BaseAddresses = new StringCollection();
                configuration.AlternateBaseAddresses = null;

                for (int ii = 0; ii < addresses.Count; ii++)
                {
                    Uri url = Utils.ParseUri(addresses[ii]);

                    if (url != null)
                    {
                        if (map.ContainsKey(url.Scheme))
                        {
                            if (configuration.AlternateBaseAddresses == null)
                            {
                                configuration.AlternateBaseAddresses = new StringCollection();
                            }

                            configuration.AlternateBaseAddresses.Add(url.ToString());
                        }
                        else
                        {
                            configuration.BaseAddresses.Add(url.ToString());
                            map.Add(url.Scheme, string.Empty);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Creates a ListOfSecurityProfiles object. 
        /// </summary>
        public static ListOfSecurityProfiles ToListOfSecurityProfiles(ServerSecurityPolicyCollection policies)
        {
            ListOfSecurityProfiles profiles = new ListOfSecurityProfiles();
            profiles.Add(CreateProfile(SecurityPolicies.None));
            profiles.Add(CreateProfile(SecurityPolicies.Basic128Rsa15));
            profiles.Add(CreateProfile(SecurityPolicies.Basic256));
            profiles.Add(CreateProfile(SecurityPolicies.Basic256Sha256));
            profiles.Add(CreateProfile(SecurityPolicies.Aes128_Sha256_RsaOaep));
            profiles.Add(CreateProfile(SecurityPolicies.Aes256_Sha256_RsaPss));

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
        public static ServerSecurityPolicyCollection FromListOfSecurityProfiles(ListOfSecurityProfiles profiles)
        {
            ServerSecurityPolicyCollection policies = new ServerSecurityPolicyCollection();

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
        /// Creates a new policy object.
        /// Always uses sign and encrypt for all security policies except none
        /// </summary>
        private static ServerSecurityPolicy CreatePolicy(string profileUri)
        {
            ServerSecurityPolicy policy = new ServerSecurityPolicy();
            policy.SecurityPolicyUri = profileUri;

            if (profileUri != null)
            {
                switch (profileUri)
                {
                    case SecurityPolicies.None:
                    {
                        policy.SecurityMode = MessageSecurityMode.None;
                        break;
                    }

                    case SecurityPolicies.Basic128Rsa15:
                    case SecurityPolicies.Basic256:
                    case SecurityPolicies.Basic256Sha256:
                    case SecurityPolicies.Aes128_Sha256_RsaOaep:
                    case SecurityPolicies.Aes256_Sha256_RsaPss:
                    {
                        policy.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                        break;
                    }

                }
            }

            return policy;
        }

        /// <summary>
        /// Creates a new policy object.
        /// </summary>
        private static Opc.Ua.Security.SecurityProfile CreateProfile(string profileUri)
        {
            Opc.Ua.Security.SecurityProfile policy = new SecurityProfile();
            policy.ProfileUri = profileUri;
            policy.Enabled = false;
            return policy;
        }
    }

    /// <summary>
    /// An identifier for a certificate.
    /// </summary>
    public partial class CertificateIdentifier
    {
        /// <summary>
        /// Gets the certificate associated with the identifier.
        /// </summary>
        public async Task<X509Certificate2> Find()
        {
            Opc.Ua.CertificateIdentifier output = SecuredApplication.FromCertificateIdentifier(this);
            return await output.Find(false);
        }

        /// <summary>
        /// Gets the certificate associated with the identifier.
        /// </summary>
        public async Task<X509Certificate2> Find(bool needPrivateKey)
        {
            Opc.Ua.CertificateIdentifier output = SecuredApplication.FromCertificateIdentifier(this);
            return await output.Find(needPrivateKey);
        }

        /// <summary>
        /// Opens the certificate store.
        /// </summary>
        public ICertificateStore OpenStore()
        {
            Opc.Ua.CertificateIdentifier output = SecuredApplication.FromCertificateIdentifier(this);
            return output.OpenStore();
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
        public ICertificateStore OpenStore()
        {
            Opc.Ua.CertificateStoreIdentifier output = SecuredApplication.FromCertificateStoreIdentifier(this);
            return output.OpenStore();
        }
    }
}
