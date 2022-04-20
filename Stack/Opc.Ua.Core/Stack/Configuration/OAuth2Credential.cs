using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

// suppress warnings until OAuth 2.0 is supported
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Opc.Ua
{
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class OAuth2ServerSettings
    {
        [DataMember(Order = 1)]
        public string ApplicationUri { get; set; }

        [DataMember(Order = 2)]
        public string ResourceId { get; set; }

        [DataMember(Order = 3)]
        public StringCollection Scopes { get; set; }
    }

    [CollectionDataContract(Name = "ListOfOAuth2ServerSettings", Namespace = Namespaces.OpcUaConfig, ItemName = "OAuth2ServerSettings")]
    public partial class OAuth2ServerSettingsCollection : List<OAuth2ServerSettings>
    {
    }

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
        [DataMember(Order = 1)]
        public string AuthorityUrl { get; set; }

        [DataMember(Order = 2)]
        public string GrantType { get; set; }

        [DataMember(Order = 3)]
        public string ClientId { get; set; }

        [DataMember(Order = 4)]
        public string ClientSecret { get; set; }

        [DataMember(Order = 5)]
        public string RedirectUrl { get; set; }

        [DataMember(Order = 6)]
        public string TokenEndpoint { get; set; }

        [DataMember(Order = 7)]
        public string AuthorizationEndpoint { get; set; }

        [DataMember(Order = 8)]
        public OAuth2ServerSettingsCollection Servers { get; set; }
        #endregion

        public OAuth2ServerSettings SelectedServer { get; set; }
    }

    [CollectionDataContract(Name = "ListOfOAuth2Credential", Namespace = Namespaces.OpcUaConfig, ItemName = "OAuth2Credential")]
    public partial class OAuth2CredentialCollection : List<OAuth2Credential>
    {
        public static OAuth2CredentialCollection Load(ApplicationConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
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

        public static OAuth2Credential FindByServerUri(ApplicationConfiguration configuration, string serverApplicationUri)
        {
            if (serverApplicationUri == null || !Uri.IsWellFormedUriString(serverApplicationUri, UriKind.Absolute))
            {
                throw new ArgumentException("Invalid application Uri specified.", nameof(serverApplicationUri));
            }

            OAuth2CredentialCollection list = Load(configuration);

            if (list != null)
            {
                foreach (var ii in list)
                {
                    if (ii.Servers != null && ii.Servers.Count > 0)
                    {
                        foreach (var jj in ii.Servers)
                        {
                            // this is too allow generic sample config files to work on any machine. 
                            // in a real system explicit host names would be used so this would have no effect.
                            var uri = jj.ApplicationUri.Replace("localhost", System.Net.Dns.GetHostName().ToLowerInvariant());

                            if (uri == serverApplicationUri)
                            {
                                var credential = new OAuth2Credential() {
                                    AuthorityUrl = ii.AuthorityUrl,
                                    GrantType = ii.GrantType,
                                    ClientId = ii.ClientId,
                                    ClientSecret = ii.ClientSecret,
                                    RedirectUrl = ii.RedirectUrl,
                                    TokenEndpoint = ii.TokenEndpoint,
                                    AuthorizationEndpoint = ii.AuthorizationEndpoint,
                                    SelectedServer = jj
                                };

                                return credential;
                            }
                        }
                    }
                }
            }

            return null;
        }

        public static OAuth2Credential FindByAuthorityUrl(ApplicationConfiguration configuration, string authorityUrl)
        {
            if (authorityUrl == null || !Uri.IsWellFormedUriString(authorityUrl, UriKind.Absolute))
            {
                throw new ArgumentException("The authority Url is invalid.", nameof(authorityUrl));
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

                    if (!uri.EndsWith("/", StringComparison.Ordinal))
                    {
                        uri += "/";
                    }

                    if (String.Equals(uri, authorityUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        var credential = new OAuth2Credential() {
                            AuthorityUrl = authorityUrl,
                            GrantType = ii.GrantType,
                            ClientId = ii.ClientId,
                            ClientSecret = ii.ClientSecret,
                            RedirectUrl = ii.RedirectUrl,
                            TokenEndpoint = ii.TokenEndpoint,
                            AuthorizationEndpoint = ii.AuthorizationEndpoint
                        };

                        return credential;
                    }
                }
            }

            return null;
        }
    }
}
