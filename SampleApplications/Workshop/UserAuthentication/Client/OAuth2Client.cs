/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * 
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using Newtonsoft.Json;
using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Quickstarts.UserAuthenticationClient
{
    public class AuthorizationClient
    {
        public ApplicationConfiguration Configuration { get; set; }

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

        public async Task<OAuth2AccessToken> RequestTokenWithClientCredentialsAsync(OAuth2Credential credential, string resourceId, string scope)
        {
            if (credential == null)
            {
                throw new ArgumentNullException("credential");
            }

            Dictionary<string, string> fields = new Dictionary<string, string>();

            fields["grant_type"] = "client_credentials";
            fields["client_id"] = credential.ClientId;
            fields["client_secret"] = credential.ClientSecret;

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
