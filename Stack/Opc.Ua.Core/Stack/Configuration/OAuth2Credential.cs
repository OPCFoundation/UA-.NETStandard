using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Xml;

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
        public ArrayOf<string> Scopes { get; set; }
    }

    [CollectionDataContract(
        Name = "ListOfOAuth2ServerSettings",
        Namespace = Namespaces.OpcUaConfig,
        ItemName = "OAuth2ServerSettings"
    )]
    public class OAuth2ServerSettingsCollection : List<OAuth2ServerSettings>;

    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class OAuth2Credential
    {
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

        public OAuth2ServerSettings SelectedServer { get; set; }
    }

    [CollectionDataContract(
        Name = "ListOfOAuth2Credential",
        Namespace = Namespaces.OpcUaConfig,
        ItemName = "OAuth2Credential"
    )]
    public class OAuth2CredentialCollection : List<OAuth2Credential>
    {
        private static readonly XmlQualifiedName s_elementName =
            new("ListOfOAuth2Credential", Namespaces.OpcUaConfig);

        /// <summary>
        /// Decodes an <see cref="OAuth2CredentialCollection"/> from an <see cref="IDecoder"/>.
        /// </summary>
        public static OAuth2CredentialCollection Decode(IDecoder decoder)
        {
            var xmlParser = (XmlParser)decoder;
            var result = new OAuth2CredentialCollection();

            xmlParser.PushNamespace(Namespaces.OpcUaConfig);
            xmlParser.ReadStartElement();

            while (xmlParser.Peek("OAuth2Credential"))
            {
                xmlParser.ReadStartElement();

                var cred = new OAuth2Credential
                {
                    AuthorityUrl = xmlParser.ReadString("AuthorityUrl"),
                    GrantType = xmlParser.ReadString("GrantType"),
                    ClientId = xmlParser.ReadString("ClientId"),
                    ClientSecret = xmlParser.ReadString("ClientSecret"),
                    RedirectUrl = xmlParser.ReadString("RedirectUrl"),
                    TokenEndpoint = xmlParser.ReadString("TokenEndpoint"),
                    AuthorizationEndpoint = xmlParser.ReadString("AuthorizationEndpoint")
                };

                if (xmlParser.Peek("Servers"))
                {
                    xmlParser.ReadStartElement();
                    var servers = new OAuth2ServerSettingsCollection();

                    while (xmlParser.Peek("OAuth2ServerSettings"))
                    {
                        xmlParser.ReadStartElement();

                        var settings = new OAuth2ServerSettings
                        {
                            ApplicationUri = xmlParser.ReadString("ApplicationUri"),
                            ResourceId = xmlParser.ReadString("ResourceId"),
                            Scopes = xmlParser.ReadStringArray("Scopes")
                        };

                        xmlParser.Skip(new XmlQualifiedName("OAuth2ServerSettings", Namespaces.OpcUaConfig));
                        servers.Add(settings);
                    }

                    xmlParser.Skip(new XmlQualifiedName("Servers", Namespaces.OpcUaConfig));
                    cred.Servers = servers;
                }

                xmlParser.Skip(new XmlQualifiedName("OAuth2Credential", Namespaces.OpcUaConfig));
                result.Add(cred);
            }

            xmlParser.Skip(new XmlQualifiedName("ListOfOAuth2Credential", Namespaces.OpcUaConfig));
            xmlParser.PopNamespace();

            return result;
        }

        /// <summary>
        /// Encodes an <see cref="OAuth2CredentialCollection"/> to an <see cref="IEncoder"/>.
        /// </summary>
        public static void Encode(IEncoder encoder, OAuth2CredentialCollection collection)
        {
            var xmlEncoder = (XmlEncoder)encoder;

            if (collection == null)
            {
                return;
            }

            foreach (OAuth2Credential cred in collection)
            {
                xmlEncoder.Push("OAuth2Credential", Namespaces.OpcUaConfig);
                xmlEncoder.WriteString("AuthorityUrl", cred.AuthorityUrl);
                xmlEncoder.WriteString("GrantType", cred.GrantType);
                xmlEncoder.WriteString("ClientId", cred.ClientId);
                xmlEncoder.WriteString("ClientSecret", cred.ClientSecret);
                xmlEncoder.WriteString("RedirectUrl", cred.RedirectUrl);
                xmlEncoder.WriteString("TokenEndpoint", cred.TokenEndpoint);
                xmlEncoder.WriteString("AuthorizationEndpoint", cred.AuthorizationEndpoint);

                if (cred.Servers != null && cred.Servers.Count > 0)
                {
                    xmlEncoder.Push("Servers", Namespaces.OpcUaConfig);

                    foreach (OAuth2ServerSettings server in cred.Servers)
                    {
                        xmlEncoder.Push("OAuth2ServerSettings", Namespaces.OpcUaConfig);
                        xmlEncoder.WriteString("ApplicationUri", server.ApplicationUri);
                        xmlEncoder.WriteString("ResourceId", server.ResourceId);

                        xmlEncoder.WriteStringArray("Scopes", server.Scopes);

                        xmlEncoder.Pop();
                    }

                    xmlEncoder.Pop();
                }

                xmlEncoder.Pop();
            }
        }

        public static OAuth2CredentialCollection Load(ApplicationConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            OAuth2CredentialCollection list = null;

            lock (configuration.PropertiesLock)
            {
                if (configuration.Properties.TryGetValue("OAuth2Credentials", out object value))
                {
                    list = value as OAuth2CredentialCollection;
                }

                if (list == null)
                {
                    list = configuration.ParseExtension<OAuth2CredentialCollection>(
                        s_elementName,
                        OAuth2CredentialCollection.Decode) ??
                        [];

                    configuration.Properties["OAuth2Credentials"] = list;
                }
            }

            return list;
        }

        public static OAuth2Credential FindByServerUri(
            ApplicationConfiguration configuration,
            string serverApplicationUri)
        {
            if (serverApplicationUri == null ||
                !Uri.IsWellFormedUriString(serverApplicationUri, UriKind.Absolute))
            {
                throw new ArgumentException(
                    "Invalid application Uri specified.",
                    nameof(serverApplicationUri));
            }

            OAuth2CredentialCollection list = Load(configuration);

            if (list != null)
            {
                foreach (OAuth2Credential ii in list)
                {
                    if (ii.Servers != null && ii.Servers.Count > 0)
                    {
                        foreach (OAuth2ServerSettings jj in ii.Servers)
                        {
                            // this is too allow generic sample config files to work on any machine.
                            // in a real system explicit host names would be used so this would have no effect.
                            string uri = jj.ApplicationUri.Replace(
                                "localhost",
                                System.Net.Dns.GetHostName().ToLowerInvariant(),
                                StringComparison.Ordinal);

                            if (uri == serverApplicationUri)
                            {
                                return new OAuth2Credential
                                {
                                    AuthorityUrl = ii.AuthorityUrl,
                                    GrantType = ii.GrantType,
                                    ClientId = ii.ClientId,
                                    ClientSecret = ii.ClientSecret,
                                    RedirectUrl = ii.RedirectUrl,
                                    TokenEndpoint = ii.TokenEndpoint,
                                    AuthorizationEndpoint = ii.AuthorizationEndpoint,
                                    SelectedServer = jj
                                };
                            }
                        }
                    }
                }
            }

            return null;
        }

        public static OAuth2Credential FindByAuthorityUrl(
            ApplicationConfiguration configuration,
            string authorityUrl)
        {
            if (authorityUrl == null || !Uri.IsWellFormedUriString(authorityUrl, UriKind.Absolute))
            {
                throw new ArgumentException("The authority Url is invalid.", nameof(authorityUrl));
            }

            if (!authorityUrl.EndsWith('/'))
            {
                authorityUrl += "/";
            }

            OAuth2CredentialCollection list = Load(configuration);

            if (list != null)
            {
                foreach (OAuth2Credential ii in list)
                {
                    // this is too allow generic sample config files to work on any machine.
                    // in a real system explicit host names would be used so this would have no effect.
                    string uri = ii.AuthorityUrl.Replace(
                        "localhost",
                        System.Net.Dns.GetHostName().ToLowerInvariant(),
                        StringComparison.Ordinal);

                    if (!uri.EndsWith('/'))
                    {
                        uri += "/";
                    }

                    if (string.Equals(uri, authorityUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        return new OAuth2Credential
                        {
                            AuthorityUrl = authorityUrl,
                            GrantType = ii.GrantType,
                            ClientId = ii.ClientId,
                            ClientSecret = ii.ClientSecret,
                            RedirectUrl = ii.RedirectUrl,
                            TokenEndpoint = ii.TokenEndpoint,
                            AuthorizationEndpoint = ii.AuthorizationEndpoint
                        };
                    }
                }
            }

            return null;
        }
    }
}
