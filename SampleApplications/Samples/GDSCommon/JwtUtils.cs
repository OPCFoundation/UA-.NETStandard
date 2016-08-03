using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Microsoft.IdentityModel.Protocols;
using System.Net.Http;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Text;

namespace Opc.Ua
{
    public class JwtUtils
    {
        public static IUserIdentity ValidateToken(Uri authorityUrl, string audiance, string jwt)
        {
            var task = ValidateTokenAsync(authorityUrl, audiance, jwt);
            task.Wait();
            return task.Result;
        }

        public static async Task<IUserIdentity> ValidateTokenAsync(Uri authorityUrl, string audiance, string jwt)
        {
            JwtSecurityToken token = new JwtSecurityToken(jwt);

            SecurityToken validatedToken = new JwtSecurityToken();
            JwtSecurityTokenHandler tokenHandler = new JwtSecurityTokenHandler();

            TokenValidationParameters validationParameters = new TokenValidationParameters
            {
                ValidAudience = audiance
            };

            ConfigurationManager<OpenIdConnectConfiguration> configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                authorityUrl.ToString() + "/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever());
            OpenIdConnectConfiguration config = await configManager.GetConfigurationAsync();
            validationParameters.ValidIssuer = new Uri(config.Issuer).ToString();
            validationParameters.IssuerSigningKeys = (IEnumerable<SecurityKey>) config.JsonWebKeySet;

            tokenHandler.ValidateToken(jwt, validationParameters, out validatedToken);

            IssuedIdentityToken issuedToken = new IssuedIdentityToken();
            issuedToken.IssuedTokenType = IssuedTokenType.JWT;
            issuedToken.DecryptedTokenData = new UTF8Encoding(false).GetBytes(((JwtSecurityToken)validatedToken).RawData);
            return new UserIdentity(issuedToken);
        }

        public static OpenIdConnectConfiguration Discover(Uri authorityUrl)
        {
            var task = DiscoverAsync(authorityUrl);
            task.Wait();
            return task.Result;
        }

        public static async Task<OpenIdConnectConfiguration> DiscoverAsync(Uri authorityUrl)
        {
            ConfigurationManager<OpenIdConnectConfiguration> configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                authorityUrl.ToString(),
                new OpenIdConnectConfigurationRetriever());
            return await configManager.GetConfigurationAsync();
        }

        public static string RequestTokenForApplication(UserTokenPolicy policy, string clientId, string clientSecret, string scope = null)
        {
            var task = RequestTokenForApplicationAsync(policy, clientId, clientSecret, scope);
            task.Wait();
            return task.Result;
        }

        public static async Task<string> RequestTokenForApplicationAsync(UserTokenPolicy policy, string clientId, string clientSecret, string scope = null)
        {
            if (policy == null)
            {
                throw new ArgumentNullException("policy");
            }

            JwtEndpointParameters parameters = new JwtEndpointParameters();
            parameters.FromJson(policy.IssuerEndpointUrl);

            var configuration = await DiscoverAsync(new Uri(parameters.AuthorityUrl + "/.well-known/openid-configuration"));

            if (String.IsNullOrEmpty(scope) && parameters.Scopes != null && parameters.Scopes.Count > 0)
            {
                scope = String.Empty;

                foreach (var entry in parameters.Scopes)
                {
                    if (scope.Length > 0)
                    {
                        scope += " ";
                    }

                    scope += entry;
                }
            }
        
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Clear();

                Dictionary<string, string> fields = new Dictionary<string, string>();

                fields["grant_type"] = "client_credentials";
                fields["client_id"] = clientId;
                fields["client_secret"] = clientSecret;

                if (!String.IsNullOrEmpty(parameters.ResourceId))
                {
                    fields["resource"] = parameters.ResourceId;
                }

                if (!String.IsNullOrEmpty(scope))
                {
                    fields["scope"] = scope;
                }

                var content = new System.Net.Http.FormUrlEncodedContent(fields);
                HttpResponseMessage response = await client.PostAsync(configuration.TokenEndpoint, content);

                if (!response.IsSuccessStatusCode)
                {
                    throw new SecurityTokenException("The could not authorize client.");
                }

                var strm = await response.Content.ReadAsStreamAsync();
                var reader = new JsonTextReader(new System.IO.StreamReader(strm));

                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.PropertyName && (string)reader.Value == "access_token")
                    {
                        if (reader.Read() && reader.TokenType == JsonToken.String)
                        {
                            return (string)reader.Value;
                        }
                    }
                }
            }

            throw new SecurityTokenException("The authorization server did not return a valid JWT.");
        }
    }

    public class JwtEndpointParameters
    {
        public string AuthorityUrl;
        public string GrantType;
        public string TokenEndpoint;
        public string ResourceId;
        public List<string> Scopes;

        public string ToJson()
        {
            var encoder = new JsonEncoder(ServiceMessageContext.GlobalContext, true);

            encoder.WriteString("authority", AuthorityUrl);
            encoder.WriteString("grantType", GrantType);
            encoder.WriteString("tokenEndpoint", TokenEndpoint);
            encoder.WriteString("resource", ResourceId);
            encoder.WriteStringArray("scopes", Scopes);

            return encoder.Close();
        }

        public void FromJson(string json)
        {
            var decoder = new JsonDecoder(json, ServiceMessageContext.GlobalContext);

            AuthorityUrl = decoder.ReadString("authority");
            GrantType = decoder.ReadString("grantType");
            TokenEndpoint = decoder.ReadString("tokenEndpoint");
            ResourceId = decoder.ReadString("resource");
            Scopes = decoder.ReadStringArray("scopes");
        }
    }
}
