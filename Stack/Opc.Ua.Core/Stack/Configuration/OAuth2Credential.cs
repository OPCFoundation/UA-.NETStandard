using System;
using System.Xml;

// suppress warnings until OAuth 2.0 is supported
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Opc.Ua
{
    [DataType(Namespace = Namespaces.OpcUaConfig)]
    public sealed partial class OAuth2ServerSettings
    {
        [DataTypeField(Order = 0)]
        public string? ApplicationUri { get; set; }

        [DataTypeField(Order = 1)]
        public string? ResourceId { get; set; }

        [DataTypeField(Order = 2)]
        public ArrayOf<string> Scopes { get; set; }
    }

    [DataType(Namespace = Namespaces.OpcUaConfig)]
    public partial class OAuth2Credential
    {
        [DataTypeField(Order = 0)]
        public string? AuthorityUrl { get; set; }

        [DataTypeField(Order = 1)]
        public string? GrantType { get; set; }

        [DataTypeField(Order = 2)]
        public string? ClientId { get; set; }

        [DataTypeField(Order = 3)]
        public string? ClientSecret { get; set; }

        [DataTypeField(Order = 4)]
        public string? RedirectUrl { get; set; }

        [DataTypeField(Order = 5)]
        public string? TokenEndpoint { get; set; }

        [DataTypeField(Order = 6)]
        public string? AuthorizationEndpoint { get; set; }

        [DataTypeField(Order = 7)]
        public ArrayOf<OAuth2ServerSettings> Servers { get; set; }

        public OAuth2ServerSettings? SelectedServer { get; set; }
    }

    public static class OAuth2CredentialCollection
    {
        private static readonly XmlQualifiedName s_elementName =
            new("ListOfOAuth2Credential", Namespaces.OpcUaConfig);

        public static ArrayOf<OAuth2Credential> Load(
            ApplicationConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            ArrayOf<OAuth2Credential> list;

            lock (configuration.PropertiesLock)
            {
                if (configuration.Properties.TryGetValue(
                    "OAuth2Credentials", out object? value) &&
                    value is ArrayOf<OAuth2Credential> existing)
                {
                    return existing;
                }

                list = configuration.ParseExtension(
                    s_elementName,
                    static decoder => decoder.ReadEncodeableArray<OAuth2Credential>(null!));

                configuration.Properties["OAuth2Credentials"] = list;
            }

            return list;
        }

        public static OAuth2Credential? FindByServerUri(
            ApplicationConfiguration configuration,
            string serverApplicationUri)
        {
            if (serverApplicationUri == null ||
                !Uri.IsWellFormedUriString(
                    serverApplicationUri, UriKind.Absolute))
            {
                throw new ArgumentException(
                    "Invalid application Uri specified.",
                    nameof(serverApplicationUri));
            }

            ArrayOf<OAuth2Credential> list = Load(configuration);

            foreach (OAuth2Credential ii in list)
            {
                foreach (OAuth2ServerSettings jj in ii.Servers)
                {
                    string uri = jj.ApplicationUri!.Replace(
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

            return null;
        }

        public static OAuth2Credential? FindByAuthorityUrl(
            ApplicationConfiguration configuration,
            string authorityUrl)
        {
            if (authorityUrl == null ||
                !Uri.IsWellFormedUriString(authorityUrl, UriKind.Absolute))
            {
                throw new ArgumentException(
                    "The authority Url is invalid.",
                    nameof(authorityUrl));
            }

            if (!authorityUrl.EndsWith('/'))
            {
                authorityUrl += "/";
            }

            ArrayOf<OAuth2Credential> list = Load(configuration);

            foreach (OAuth2Credential ii in list)
            {
                string uri = ii.AuthorityUrl!.Replace(
                    "localhost",
                    System.Net.Dns.GetHostName().ToLowerInvariant(),
                    StringComparison.Ordinal);

                if (!uri.EndsWith('/'))
                {
                    uri += "/";
                }

                if (string.Equals(
                    uri, authorityUrl,
                    StringComparison.OrdinalIgnoreCase))
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

            return null;
        }
    }
}
