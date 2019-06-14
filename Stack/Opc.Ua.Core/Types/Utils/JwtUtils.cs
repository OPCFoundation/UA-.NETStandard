/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
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

using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <remark />
    public class JwtUtils
    {
        /// <remark />
        public static IUserIdentity ValidateToken(Uri authorityUrl, X509Certificate2 authorityCertificate, string issuerUri, string audiance, string jwt)
        {
            return ValidateTokenAsync(authorityUrl, authorityCertificate, issuerUri, audiance, jwt).Result;
        }

        /// <remark />
        public static async Task<IUserIdentity> ValidateTokenAsync(Uri authorityUrl, X509Certificate2 authorityCertificate, string issuerUri, string audiance, string jwt)
        {
            JwtSecurityToken token = new JwtSecurityToken(jwt);

            SecurityToken validatedToken = new JwtSecurityToken();
            JwtSecurityTokenHandler tokenHandler = new JwtSecurityTokenHandler();

            TokenValidationParameters validationParameters = new TokenValidationParameters
            {
                ValidateAudience = true,
                ValidAudience = audiance,
                ValidateLifetime = true,
                ClockSkew = new TimeSpan(0, 5, 0)
            };

            if (authorityCertificate != null)
            {
                validationParameters.ValidateIssuer = true;
                validationParameters.ValidIssuer = issuerUri;
                validationParameters.IssuerSigningKey = new X509SecurityKey(authorityCertificate);
                tokenHandler.ValidateToken(jwt, validationParameters, out validatedToken);
            }
            else
            {
                string metadataAddress = authorityUrl.ToString() + "/.well-known/openid-configuration";
                OpenIdConnectConfiguration openidConfig = await OpenIdConnectConfigurationRetriever.GetAsync(
                   metadataAddress, CancellationToken.None);
                validationParameters.ValidIssuer = new Uri(openidConfig.Issuer).ToString();
                validationParameters.IssuerSigningKeys = new List<SecurityKey>(openidConfig.SigningKeys);
                tokenHandler.ValidateToken(jwt, validationParameters, out validatedToken);
            }

            return new UserIdentity(validatedToken);
        }
    }

    /// <remark />
    public class JwtEndpointParameters
    {
        /// <remark />
        public string AuthorityUrl;
        /// <remark />
        public string AuthorityProfileUri;
        /// <remark />
        public string TokenEndpoint;
        /// <remark />
        public string AuthorizationEndpoint;
        /// <remark />
        public List<string> RequestTypes;
        /// <remark />
        public string ResourceId;
        /// <remark />
        public List<string> Scopes;

        /// <remark />
        public string ToJson()
        {
            var encoder = new JsonEncoder(ServiceMessageContext.GlobalContext, true);

            encoder.WriteString("ua:authorityUrl", AuthorityUrl);
            encoder.WriteString("ua:authorityProfileUri", AuthorityProfileUri);
            encoder.WriteString("ua:tokenEndpoint", TokenEndpoint);
            encoder.WriteString("ua:authorizationEndpoint", AuthorizationEndpoint);
            encoder.WriteStringArray("ua:requestTypes", RequestTypes);
            encoder.WriteString("ua:resource", ResourceId);
            encoder.WriteStringArray("ua:scopes", Scopes);

            return encoder.CloseAndReturnText();
        }

        /// <remark />
        public void FromJson(string json)
        {
            var decoder = new JsonDecoder(json, ServiceMessageContext.GlobalContext);

            AuthorityUrl = decoder.ReadString("ua:authorityUrl");
            AuthorityProfileUri = decoder.ReadString("ua:authorityProfileUri");
            TokenEndpoint = decoder.ReadString("ua:tokenEndpoint");
            AuthorizationEndpoint = decoder.ReadString("ua:authorizationEndpoint");
            RequestTypes = decoder.ReadStringArray("ua:requestTypes");
            ResourceId = decoder.ReadString("ua:resourceId");
            Scopes = decoder.ReadStringArray("ua:scopes");
        }
    }

    /// <remark />
    public static class JwtConstants
    {
        /// <remark />
        public const string OAuth2AuthorizationCode = "authorization_code";
        /// <remark />
        public const string OAuth2ClientCredentials = "client_credentials";
    }
}
