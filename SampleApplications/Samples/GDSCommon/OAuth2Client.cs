using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Newtonsoft.Json;
using Opc.Ua.Client.Controls;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;

namespace Opc.Ua.Gds
{
    public class OAuth2Client
    {
        public ApplicationConfiguration Configuration { get; set; }

        public static async Task<UserIdentity> GetIdentityToken(ApplicationConfiguration configuration, string endpointUrl)
        {
            // get an endpoint to use.
            var endpoint = ClientUtils.SelectEndpoint(endpointUrl, true);

            // find user token policy that supports JWTs.
            JwtEndpointParameters parameters = null;

            foreach (var policy in endpoint.UserIdentityTokens)
            {
                if (policy.IssuedTokenType == "http://opcfoundation.org/UA/UserTokenPolicy#JWT")
                {
                    parameters = new JwtEndpointParameters();
                    parameters.FromJson(policy.IssuerEndpointUrl);
                    break;
                }
            }

            if (parameters == null)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError, "No JWT UserTokenPolicy specified for the selected GDS.");
            }

            // set the default resource.
            if (String.IsNullOrEmpty(parameters.ResourceId))
            {
                parameters.ResourceId = endpoint.Server.ApplicationUri;
            }

            // get the authorization server that the GDS actually uses.
            var gdsCredentials = OAuth2CredentialCollection.FindByAuthorityUrl(configuration, parameters.AuthorityUrl);

            // create default credentials from the server endpoint.
            if (gdsCredentials == null)
            {
                gdsCredentials = new OAuth2Credential();
            }

            // override with settings provided by server.
            gdsCredentials.AuthorityUrl = parameters.AuthorityUrl;
            gdsCredentials.GrantType = parameters.GrantType;
            gdsCredentials.TokenEndpoint = parameters.TokenEndpoint;

            JwtSecurityToken jwt = null;

            // need to get credentials from an external authority.
            if (gdsCredentials != null && gdsCredentials.GrantType == "site_token")
            {
                // need to find an OAuth2 server that can supply credentials.
                var azureCredentials = OAuth2CredentialCollection.FindByServerUri(configuration, parameters.ResourceId);

                if (azureCredentials == null)
                {
                    throw new ServiceResultException(StatusCodes.BadConfigurationError, "No OAuth2 configuration specified for the selected GDS.");
                }

                // prompt user to provide credentials.
                var azureToken = new OAuth2CredentialsDialog().ShowDialog(azureCredentials);

                if (azureToken == null)
                {
                    return null;
                }

                jwt = new JwtSecurityToken(azureToken.AccessToken);
                IssuedIdentityToken issuedToken = new IssuedIdentityToken();
                issuedToken.IssuedTokenType = IssuedTokenType.JWT;
                issuedToken.DecryptedTokenData = new UTF8Encoding(false).GetBytes(jwt.RawData);
                var azureIdentity = new UserIdentity(issuedToken);

                // log in using site token.
                OAuth2Client client = new OAuth2Client() { Configuration = configuration };
                var certificate = await client.Configuration.SecurityConfiguration.ApplicationCertificate.Find(true);
                var gdsAccessToken = await client.RequestTokenWithWithSiteTokenAsync(gdsCredentials, certificate, azureToken.AccessToken, parameters.ResourceId, "gdsadmin");
                JwtSecurityToken gdsToken = new JwtSecurityToken(gdsAccessToken.AccessToken);
                issuedToken = new IssuedIdentityToken();
                issuedToken.IssuedTokenType = IssuedTokenType.JWT;
                issuedToken.DecryptedTokenData = new UTF8Encoding(false).GetBytes(gdsToken.RawData);
                return new UserIdentity(issuedToken);
            }

            // attempt to log in directly
            else
            {
                string username = null;
                string password = null;

                // TBD - Prompt User to Provide.

                OAuth2Client client = new OAuth2Client() { Configuration = configuration };
                var gdsAccessToken = await client.RequestTokenWithWithUserNameAsync(gdsCredentials, username, password, parameters.ResourceId, "gdsadmin");
                JwtSecurityToken gdsToken = new JwtSecurityToken(gdsAccessToken.AccessToken);
                IssuedIdentityToken issuedToken = new IssuedIdentityToken();
                issuedToken.IssuedTokenType = IssuedTokenType.JWT;
                issuedToken.DecryptedTokenData = new UTF8Encoding(false).GetBytes(gdsToken.RawData);
                return new UserIdentity(issuedToken);
            }
        }

        public async Task<OAuth2AccessToken> RequestTokenWithAuthenticationCodeAsync(OAuth2Credential credential, string resourceId, string authenticationCode)
        {
            if (credential == null)
            {
                throw new ArgumentNullException("credential");
            }

            Dictionary<string, string> fields = new Dictionary<string, string>();

            fields["grant_type"] = "authorization_code";
            fields["code"] = authenticationCode;
            fields["client_id"] = credential.ClientId;

            if (!String.IsNullOrEmpty(credential.ClientSecret))
            {
                fields["client_secret"] = credential.ClientSecret;
            }

            if (!String.IsNullOrEmpty(credential.RedirectUrl))
            {
                fields["redirect_uri"] = credential.RedirectUrl;
            }

            if (!String.IsNullOrEmpty(resourceId))
            {
                fields["resource"] = resourceId;
            }

            var url = new UriBuilder(credential.AuthorityUrl);
            url.Path += credential.TokenEndpoint;
            return await RequestTokenAsync(url.Uri, fields);
        }

        public async Task<OAuth2AccessToken> RequestTokenWithWithUserNameAsync(OAuth2Credential credential, string userName, string password, string resourceId, string scope)
        {
            if (credential == null)
            {
                throw new ArgumentNullException("credential");
            }

            Dictionary<string, string> fields = new Dictionary<string, string>();

            fields["grant_type"] = "password";
            fields["client_id"] = credential.ClientId;
            fields["client_secret"] = credential.ClientSecret;
            fields["username"] = userName;
            fields["password"] = password;
            
            if (!String.IsNullOrEmpty(credential.RedirectUrl))
            {
                fields["redirect_uri"] = credential.RedirectUrl;
            }

            if (!String.IsNullOrEmpty(resourceId))
            {
                fields["resource"] = resourceId;
            }

            if (!String.IsNullOrEmpty(scope))
            {
                fields["scope"] = scope;
            }

            var url = new UriBuilder(credential.AuthorityUrl);
            url.Path += credential.TokenEndpoint;
            return await RequestTokenAsync(url.Uri, fields);
        }

        public async Task<OAuth2AccessToken> RequestTokenWithWithSiteTokenAsync(OAuth2Credential credential, X509Certificate2 certificate, string accessToken, string resourceId, string scope)
        {
            if (credential == null)
            {
                throw new ArgumentNullException("credential");
            }

            if (certificate == null)
            {
                throw new ArgumentNullException("certificate");
            }

            Dictionary<string, string> fields = new Dictionary<string, string>();

            var clientId = certificate.GetRawCertData();

            var random = new RNGCryptoServiceProvider();
            var nonce = new byte[32];
            random.GetBytes(nonce);

            var dataToSign = Utils.Append(new UTF8Encoding(false).GetBytes(accessToken), nonce);
            var signature = RsaUtils.RsaPkcs15Sha1_Sign(new ArraySegment<byte>(dataToSign), certificate);

            fields["grant_type"] = "urn:opcfoundation.org:oauth2:site_token";
            fields["client_id"] = Convert.ToBase64String(clientId);
            fields["client_secret"] = Convert.ToBase64String(signature);
            fields["nonce"] = Convert.ToBase64String(nonce);
            fields["access_token"] = accessToken;

            if (!String.IsNullOrEmpty(credential.RedirectUrl))
            {
                fields["redirect_uri"] = credential.RedirectUrl;
            }

            if (!String.IsNullOrEmpty(resourceId))
            {
                fields["resource"] = resourceId;
            }

            if (!String.IsNullOrEmpty(scope))
            {
                fields["scope"] = scope;
            }

            var url = new UriBuilder(credential.AuthorityUrl);
            url.Path += credential.TokenEndpoint;
            return await RequestTokenAsync(url.Uri, fields);
        }

        private async Task<OAuth2AccessToken> RequestTokenAsync(Uri url, Dictionary<string, string> fields)
        {
            HttpClient client = new HttpClient();
            client.BaseAddress = url;
            client.DefaultRequestHeaders.Accept.Clear();

            var content = new System.Net.Http.FormUrlEncodedContent(fields);
            HttpResponseMessage response = await client.PostAsync("", content);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(response.ReasonPhrase);
            }

            OAuth2AccessToken token = new OAuth2AccessToken();

            var strm = await response.Content.ReadAsStreamAsync();
            var reader = new JsonTextReader(new System.IO.StreamReader(strm));

            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.PropertyName)
                {
                    string name = (string)reader.Value;
                    reader.Read();

                    switch (name)
                    {
                        case "error":
                        {
                            throw new HttpRequestException((string)reader.Value);
                        }

                        case "resource":
                        {
                            token.ResourceId = (string)reader.Value;
                            break;
                        }

                        case "scope":
                        {
                            token.Scope = (string)reader.Value;
                            break;
                        }

                        case "refresh_token":
                        {
                            token.RefreshToken = (string)reader.Value;
                            break;
                        }

                        case "expires_on":
                        {
                            long seconds = 0;

                            if (Int64.TryParse((string)reader.Value, out seconds))
                            {
                                token.ExpiresOn = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds);
                            }

                            break;
                        }

                        case "access_token":
                        {
                            token.AccessToken = (string)reader.Value;
                            break;
                        }
                    }
                }
            }

            return token;
        }
    }

    public class OAuth2AccessToken
    {
        public string AccessToken;
        public DateTime ExpiresOn;
        public string RefreshToken;
        public string ResourceId;
        public string Scope;
    }
}
