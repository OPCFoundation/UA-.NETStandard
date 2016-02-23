/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

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
                        policy.SecurityLevel = 0;
                        break;
                    }

                    case SecurityPolicies.Basic128Rsa15:
                    {
                        policy.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                        policy.SecurityLevel = 1;
                        break;
                    }

                    case SecurityPolicies.Basic256:
                    {
                        policy.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                        policy.SecurityLevel = 2;
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

        /// <summary>
        /// Gets the private key file path.
        /// </summary>
        public async Task<string> GetPrivateKeyFilePath()
        {
            Opc.Ua.CertificateIdentifier output = SecuredApplication.FromCertificateIdentifier(this);
            return await output.GetPrivateKeyFilePath();
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
