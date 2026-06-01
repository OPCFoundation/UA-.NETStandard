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

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Opc.Ua.Identity
{
    /// <summary>
    /// Parsed view of the <c>IssuerEndpointUrl</c> JSON object carried by
    /// a JWT <see cref="UserTokenPolicy"/> per OPC 10000-6 §6.5.2.2. Tells
    /// a client which Authorization Service to call, which protocol to
    /// use, and which scopes are understood by the Server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>IssuerEndpointUrl</c> of a JWT <see cref="UserTokenPolicy"/>
    /// is, contrary to its name, NOT a plain URL — Part 6 §6.5.2 Table 55
    /// defines it as a JSON object that carries Authorization Service
    /// metadata (authority URI, resource URI, request type, scopes,
    /// audience). This type provides a strongly-typed view of that
    /// payload plus a parser at
    /// <see cref="Parse(string)"/>.
    /// </para>
    /// <para>
    /// The object is intentionally a plain DTO; it carries no secrets
    /// and is safe to log, persist, and pass across boundaries.
    /// </para>
    /// </remarks>
    public sealed record AuthorizationServerMetadata
    {
        /// <summary>
        /// Authority URI of the Authorization Service that issues access
        /// tokens for this server (Part 6 Table 55 <c>authorityUri</c>).
        /// For an OIDC provider this is the issuer URI from which
        /// <c>.well-known/openid-configuration</c> can be fetched.
        /// </summary>
        public string? AuthorityUri { get; init; }

        /// <summary>
        /// OPC UA-specific resource URI that identifies the target
        /// server / resource the access token must be scoped to
        /// (Part 6 Table 55 <c>ua:resourceUri</c>). Typically the
        /// <c>ApplicationUri</c> of the target server.
        /// </summary>
        public string? ResourceUri { get; init; }

        /// <summary>
        /// The OAuth2 request type. Typical values are
        /// <c>authorization_code</c>, <c>client_credentials</c>, or
        /// <c>password</c> (Part 6 Table 55 <c>requestTypes</c>).
        /// Multiple values mean the Authorization Service supports more
        /// than one flow.
        /// </summary>
        public IReadOnlyList<string> RequestTypes { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Scopes understood by the server. Mirrors the OIDC
        /// <c>scopes_supported</c> metadata field. When empty the client
        /// may request any scope supported by the Authorization Service
        /// (Part 6 Table 55 <c>scopes</c>).
        /// </summary>
        public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Token endpoint URI (OIDC <c>token_endpoint</c>) when the
        /// Authorization Service publishes it directly inside the
        /// <c>IssuerEndpointUrl</c> rather than via the
        /// <c>.well-known/openid-configuration</c> document. Optional.
        /// </summary>
        public string? TokenEndpoint { get; init; }

        /// <summary>
        /// Authorization endpoint URI (OIDC
        /// <c>authorization_endpoint</c>) when the Authorization Service
        /// publishes it directly. Optional.
        /// </summary>
        public string? AuthorizationEndpoint { get; init; }

        /// <summary>
        /// OAuth2 audience expected by the Authorization Service when
        /// issuing the access token. Typically the server's
        /// <c>ApplicationUri</c>. Optional — many providers infer this
        /// from <see cref="ResourceUri"/>.
        /// </summary>
        public string? Audience { get; init; }

        /// <summary>
        /// JWKS URI (OIDC <c>jwks_uri</c>) when the Authorization Service
        /// publishes its signing keys outside the OIDC discovery
        /// document. Optional.
        /// </summary>
        public string? JwksUri { get; init; }

        /// <summary>
        /// Raw additional fields preserved verbatim from the source
        /// JSON. Implementations may consult this for provider-specific
        /// extensions that are not modelled in this DTO (for example,
        /// Entra-specific <c>tenant_id</c> or PKCE configuration).
        /// </summary>
        public IReadOnlyDictionary<string, JsonElement> AdditionalFields { get; init; }
            = new Dictionary<string, JsonElement>();

        /// <summary>
        /// Parses the OPC 10000-6 §6.5.2.2 JSON payload that lives in the
        /// <see cref="UserTokenPolicy.IssuerEndpointUrl"/> string when
        /// the token type is JWT.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Per Part 6 Table 55 the well-known JSON field names are:
        /// <c>authorityUri</c>, <c>ua:resourceUri</c>, <c>requestTypes</c>,
        /// <c>scopes</c>, optionally <c>tokenEndpoint</c>,
        /// <c>authorizationEndpoint</c>, <c>audience</c>,
        /// <c>jwksUri</c>. The parser is lenient with respect to casing
        /// and the <c>ua:</c> prefix and tolerates unknown fields (they
        /// are captured into <see cref="AdditionalFields"/>).
        /// </para>
        /// <para>
        /// When <paramref name="payload"/> is null, empty, or whitespace
        /// the parser returns an empty metadata instance rather than
        /// throwing. This matches the spec's treatment of an optional
        /// payload.
        /// </para>
        /// </remarks>
        /// <param name="payload">The raw <c>IssuerEndpointUrl</c> string.</param>
        /// <exception cref="ServiceResultException">
        /// <c>BadDecodingError</c> when the JSON is malformed.
        /// </exception>
        public static AuthorizationServerMetadata Parse(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new AuthorizationServerMetadata();
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(payload!);
            }
            catch (JsonException e)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    e,
                    "Failed to parse IssuerEndpointUrl JSON payload per OPC 10000-6 §6.5.2.2.");
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "IssuerEndpointUrl payload must be a JSON object per OPC 10000-6 §6.5.2.2.");
                }

                string? authorityUri = null;
                string? resourceUri = null;
                IReadOnlyList<string> requestTypes = Array.Empty<string>();
                IReadOnlyList<string> scopes = Array.Empty<string>();
                string? tokenEndpoint = null;
                string? authorizationEndpoint = null;
                string? audience = null;
                string? jwksUri = null;
                Dictionary<string, JsonElement>? additional = null;

                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    string name = property.Name;
                    if (name.StartsWith("ua:", StringComparison.OrdinalIgnoreCase))
                    {
                        name = name[3..];
                    }

                    switch (name.ToLowerInvariant())
                    {
                        case "authorityuri":
                        case "issuer":
                            authorityUri = ReadString(property.Value);
                            break;
                        case "resourceuri":
                            resourceUri = ReadString(property.Value);
                            break;
                        case "requesttypes":
                        case "request_types":
                        case "request_type":
                        case "requesttype":
                            requestTypes = ReadStringArray(property.Value);
                            break;
                        case "scopes":
                        case "scopes_supported":
                            scopes = ReadStringArray(property.Value);
                            break;
                        case "tokenendpoint":
                        case "token_endpoint":
                            tokenEndpoint = ReadString(property.Value);
                            break;
                        case "authorizationendpoint":
                        case "authorization_endpoint":
                            authorizationEndpoint = ReadString(property.Value);
                            break;
                        case "audience":
                        case "aud":
                            audience = ReadString(property.Value);
                            break;
                        case "jwksuri":
                        case "jwks_uri":
                            jwksUri = ReadString(property.Value);
                            break;
                        default:
                            additional ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                            additional[property.Name] = property.Value.Clone();
                            break;
                    }
                }

                return new AuthorizationServerMetadata
                {
                    AuthorityUri = authorityUri,
                    ResourceUri = resourceUri,
                    RequestTypes = requestTypes,
                    Scopes = scopes,
                    TokenEndpoint = tokenEndpoint,
                    AuthorizationEndpoint = authorizationEndpoint,
                    Audience = audience,
                    JwksUri = jwksUri,
                    AdditionalFields = additional ??
                        (IReadOnlyDictionary<string, JsonElement>)
                            new Dictionary<string, JsonElement>()
                };
            }
        }

        /// <summary>
        /// Tries to extract authorization server metadata from a JWT
        /// <see cref="UserTokenPolicy"/>. Returns <see langword="true"/>
        /// when <paramref name="policy"/> is a JWT policy with a parseable
        /// <see cref="UserTokenPolicy.IssuerEndpointUrl"/> payload.
        /// </summary>
        public static bool TryFromPolicy(
            UserTokenPolicy? policy,
            out AuthorizationServerMetadata metadata)
        {
            if (policy == null ||
                policy.TokenType != UserTokenType.IssuedToken ||
                !string.Equals(
                    policy.IssuedTokenType,
                    Profiles.JwtUserToken,
                    StringComparison.Ordinal))
            {
                metadata = new AuthorizationServerMetadata();
                return false;
            }

            metadata = Parse(policy.IssuerEndpointUrl);
            return true;
        }

        private static string? ReadString(JsonElement element)
        {
            return element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;
        }

        private static IReadOnlyList<string> ReadStringArray(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                string? value = element.GetString();
                return string.IsNullOrEmpty(value)
                    ? Array.Empty<string>()
                    : [value!];
            }

            if (element.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var list = new List<string>(element.GetArrayLength());
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    string? value = item.GetString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        list.Add(value!);
                    }
                }
            }
            return list;
        }
    }
}
