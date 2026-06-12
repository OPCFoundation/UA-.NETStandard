/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
 *
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

using NUnit.Framework;
using Opc.Ua.Identity;

namespace Opc.Ua.Core.Tests.Security.Identity
{
    /// <summary>
    /// Tests for <see cref="AuthorizationServerMetadata"/> JSON parsing
    /// per OPC 10000-6 §6.5.2.2.
    /// </summary>
    [TestFixture]
    [Category("Identity")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class AuthorizationServerMetadataTests
    {
        [Test]
        public void ParseNullReturnsEmpty()
        {
            var metadata = AuthorizationServerMetadata.Parse(null);
            Assert.That(metadata.AuthorityUri, Is.Null);
            Assert.That(metadata.ResourceUri, Is.Null);
            Assert.That(metadata.RequestTypes, Is.Empty);
            Assert.That(metadata.Scopes, Is.Empty);
        }

        [Test]
        public void ParseEmptyReturnsEmpty()
        {
            var metadata = AuthorizationServerMetadata.Parse(string.Empty);
            Assert.That(metadata.AuthorityUri, Is.Null);
        }

        [Test]
        public void ParseWhitespaceReturnsEmpty()
        {
            var metadata = AuthorizationServerMetadata.Parse("   \t \r\n");
            Assert.That(metadata.AuthorityUri, Is.Null);
        }

        private static readonly string[] s_canonicalRequestTypes =
        [
            "authorization_code",
            "client_credentials"
        ];

        private static readonly string[] s_canonicalScopes =
        [
            "UAPubSub",
            "read:nodes"
        ];

        private static readonly string[] s_singleClientCredentials = ["client_credentials"];
        private static readonly string[] s_oidcScopes = ["openid", "profile"];

        [Test]
        public void ParseExtractsCanonicalFields()
        {
            const string payload = /*lang=json,strict*/ """
                {
                    "authorityUri": "https://login.microsoftonline.com/contoso.onmicrosoft.com",
                    "ua:resourceUri": "urn:opcfoundation:test:server",
                    "requestTypes": ["authorization_code", "client_credentials"],
                    "scopes": ["UAPubSub", "read:nodes"]
                }
                """;

            var metadata = AuthorizationServerMetadata.Parse(payload);

            Assert.That(
                metadata.AuthorityUri,
                Is.EqualTo("https://login.microsoftonline.com/contoso.onmicrosoft.com"));
            Assert.That(metadata.ResourceUri, Is.EqualTo("urn:opcfoundation:test:server"));
            Assert.That(metadata.RequestTypes, Is.EqualTo(s_canonicalRequestTypes));
            Assert.That(metadata.Scopes, Is.EqualTo(s_canonicalScopes));
        }

        [Test]
        public void ParseAcceptsResourceUriWithoutUaPrefix()
        {
            const string payload = /*lang=json,strict*/ """{"resourceUri":"urn:opcfoundation:test"}""";

            var metadata = AuthorizationServerMetadata.Parse(payload);

            Assert.That(metadata.ResourceUri, Is.EqualTo("urn:opcfoundation:test"));
        }

        [Test]
        public void ParseAcceptsOidcStyleFieldNames()
        {
            const string payload = /*lang=json,strict*/ """
                {
                    "issuer": "https://idp.example.com",
                    "token_endpoint": "https://idp.example.com/token",
                    "authorization_endpoint": "https://idp.example.com/authorize",
                    "scopes_supported": ["openid", "profile"],
                    "jwks_uri": "https://idp.example.com/.well-known/jwks.json"
                }
                """;

            var metadata = AuthorizationServerMetadata.Parse(payload);

            Assert.That(metadata.AuthorityUri, Is.EqualTo("https://idp.example.com"));
            Assert.That(metadata.TokenEndpoint, Is.EqualTo("https://idp.example.com/token"));
            Assert.That(
                metadata.AuthorizationEndpoint,
                Is.EqualTo("https://idp.example.com/authorize"));
            Assert.That(metadata.Scopes, Is.EqualTo(s_oidcScopes));
            Assert.That(metadata.JwksUri, Is.EqualTo("https://idp.example.com/.well-known/jwks.json"));
        }

        [Test]
        public void ParseAcceptsSingleStringForRequestTypes()
        {
            // Servers that emit a single value as a bare string (not an array)
            // are still tolerated.
            const string payload = /*lang=json,strict*/ """{"requestTypes":"client_credentials"}""";

            var metadata = AuthorizationServerMetadata.Parse(payload);

            Assert.That(metadata.RequestTypes, Is.EqualTo(s_singleClientCredentials));
        }

        [Test]
        public void ParseCapturesUnknownFieldsIntoAdditionalFields()
        {
            const string payload = /*lang=json,strict*/ """
                {
                    "authorityUri": "https://idp.example.com",
                    "tenant_id": "0001-aaaa",
                    "pkce_required": true
                }
                """;

            var metadata = AuthorizationServerMetadata.Parse(payload);

            Assert.That(metadata.AdditionalFields.ContainsKey("tenant_id"), Is.True);
            Assert.That(metadata.AdditionalFields.ContainsKey("pkce_required"), Is.True);
            Assert.That(
                metadata.AdditionalFields["tenant_id"].GetString(),
                Is.EqualTo("0001-aaaa"));
            Assert.That(metadata.AdditionalFields["pkce_required"].GetBoolean(), Is.True);
        }

        [Test]
        public void ParseMalformedJsonThrowsBadDecodingError()
        {
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => AuthorizationServerMetadata.Parse("{not valid"));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void ParseRejectsNonObjectRoot()
        {
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => AuthorizationServerMetadata.Parse("[1,2,3]"));
            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void TryFromPolicyReturnsFalseForNonJwtPolicy()
        {
            var policy = new UserTokenPolicy(UserTokenType.UserName)
            {
                PolicyId = "Basic256",
                IssuedTokenType = "ignored"
            };

            bool result = AuthorizationServerMetadata.TryFromPolicy(
                policy,
                out AuthorizationServerMetadata metadata);

            Assert.That(result, Is.False);
            Assert.That(metadata.AuthorityUri, Is.Null);
        }

        [Test]
        public void TryFromPolicyReturnsTrueForJwtPolicyAndParsesPayload()
        {
            var policy = new UserTokenPolicy(UserTokenType.IssuedToken)
            {
                PolicyId = "Jwt_Aad",
                IssuedTokenType = Profiles.JwtUserToken,
                IssuerEndpointUrl = /*lang=json,strict*/ """{"authorityUri":"https://idp.example.com"}"""
            };

            bool result = AuthorizationServerMetadata.TryFromPolicy(
                policy,
                out AuthorizationServerMetadata metadata);

            Assert.That(result, Is.True);
            Assert.That(metadata.AuthorityUri, Is.EqualTo("https://idp.example.com"));
        }

        [Test]
        public void TryFromPolicyReturnsFalseForNullPolicy()
        {
            bool result = AuthorizationServerMetadata.TryFromPolicy(
                null,
                out AuthorizationServerMetadata metadata);

            Assert.That(result, Is.False);
            Assert.That(metadata.AuthorityUri, Is.Null);
            Assert.That(metadata.ResourceUri, Is.Null);
            Assert.That(metadata.RequestTypes, Is.Empty);
            Assert.That(metadata.Scopes, Is.Empty);
        }
    }
}
