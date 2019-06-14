/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.

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
using System.Linq;
using System.Runtime.Serialization;

namespace Opc.Ua
{
    /// <remarks/>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class OAuth2ServerSettings
    {
        /// <remarks/>
        [DataMember(Order = 1)]
        public string ApplicationUri { get; set; }

        /// <remarks/>
        [DataMember(Order = 2)]
        public string ResourceId { get; set; }

        /// <remarks/>
        [DataMember(Order = 3)]
        public StringCollection Scopes { get; set; }
    }

    /// <remarks/>
    [CollectionDataContract(Name = "ListOfOAuth2ServerSettings", Namespace = Namespaces.OpcUaConfig, ItemName = "OAuth2ServerSettings")]
    public partial class OAuth2ServerSettingsCollection : List<OAuth2ServerSettings>
    {
    }

    /// <remarks/>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class OAuth2Credential
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public OAuth2Credential()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing()]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
        }
        #endregion

        #region Public Properties
        /// <remarks/>
        [DataMember(Order = 1)]
        public string AuthorityUrl { get; set; }

        /// <remarks/>
        [DataMember(Order = 2)]
        public string GrantType { get; set; }

        /// <remarks/>
        [DataMember(Order = 3)]
        public string ClientId { get; set; }

        /// <remarks/>
        [DataMember(Order = 4)]
        public string ClientSecret { get; set; }

        /// <remarks/>
        [DataMember(Order = 5)]
        public string RedirectUrl { get; set; }

        /// <remarks/>
        [DataMember(Order = 6)]
        public string TokenEndpoint { get; set; }

        /// <remarks/>
        [DataMember(Order = 7)]
        public string AuthorizationEndpoint { get; set; }

        /// <remarks/>
        [DataMember(Order = 8)]
        public string ServerResourceId { get; set; }
        #endregion
    }

    /// <remarks/>
    [CollectionDataContract(Name = "ListOfOAuth2Credential", Namespace = Namespaces.OpcUaConfig, ItemName = "OAuth2Credential")]
    public partial class OAuth2CredentialCollection : List<OAuth2Credential>
    {
        /// <remarks/>
        public static OAuth2CredentialCollection Load(ApplicationConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException("configuration");
            }

            OAuth2CredentialCollection list = null;

            lock (configuration.PropertiesLock)
            {
                object value = null;

                if (configuration.Properties.TryGetValue("OAuth2Credentials", out value))
                {
                    list = value as OAuth2CredentialCollection;
                }

                if (list == null)
                {
                    list = configuration.ParseExtension<OAuth2CredentialCollection>();

                    if (list == null)
                    {
                        list = new OAuth2CredentialCollection();
                    }

                    configuration.Properties["OAuth2Credentials"] = list;
                }
            }

            return list;
        }

        /// <remarks/>
        public static OAuth2Credential FindByAuthorityUrl(ApplicationConfiguration configuration, string authorityUrl)
        {
            if (authorityUrl == null || !Uri.IsWellFormedUriString(authorityUrl, UriKind.Absolute))
            {
                throw new ArgumentException("authorityUrl");
            }

            if (!authorityUrl.EndsWith("/"))
            {
                authorityUrl += "/";
            }

            OAuth2CredentialCollection list = Load(configuration);

            if (list != null)
            {
                foreach (var ii in list)
                {
                    // this is too allow generic sample config files to work on any machine. 
                    // in a real system explicit host names would be used so this would have no effect.
                    var uri = ii.AuthorityUrl.Replace("localhost", System.Net.Dns.GetHostName().ToLowerInvariant());

                    if (!uri.EndsWith("/"))
                    {
                        uri += "/";
                    }

                    if (String.Compare(uri, authorityUrl, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        var credential = new OAuth2Credential()
                        {
                            AuthorityUrl = authorityUrl,
                            GrantType = ii.GrantType,
                            ClientId = ii.ClientId,
                            ClientSecret = ii.ClientSecret,
                            RedirectUrl = ii.RedirectUrl,
                            TokenEndpoint = ii.TokenEndpoint,
                            AuthorizationEndpoint = ii.AuthorizationEndpoint,
                            ServerResourceId = ii.ServerResourceId
                        };

                        return credential;
                    }
                }
            }

            return null;
        }
    }
}
