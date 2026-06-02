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
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
#if NET8_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
#endif
using Opc.Ua.Identity;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Experimental vendor authenticator that accepts a KeyCredential proof as an issued user token.
    /// </summary>
    /// <remarks>
    /// EXPERIMENTAL — see Docs/KeyCredentialService.md. This authenticator handles the vendor profile URI
    /// <c>urn:opcfoundation:netstandard:profile:authentication:keycredential</c>. It is not an OPC UA
    /// Part 6 §6.5.3 conformance claim and should be enabled only in closed deployments.
    /// </remarks>
#if NET8_0_OR_GREATER
    [Experimental(
        "OPCUA_EXPERIMENTAL_KC_BRIDGE",
        UrlFormat = "https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/KeyCredentialService.md")]
#endif
    public sealed class KeyCredentialBridgeAuthenticator : IUserTokenAuthenticator
    {
        /// <summary>
        /// Vendor profile URI handled by the default authenticator configuration.
        /// </summary>
        public const string VendorKeyCredentialProfileUri = KeyCredentialBridgeOptions.DefaultProfileUri;

        private readonly IKeyCredentialStore m_store;
        private readonly KeyCredentialBridgeOptions m_options;

        /// <summary>
        /// Creates the bridge authenticator.
        /// </summary>
        public KeyCredentialBridgeAuthenticator(
            IKeyCredentialStore store,
            KeyCredentialBridgeOptions? options = null)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_options = options ?? new KeyCredentialBridgeOptions();
        }

        /// <inheritdoc/>
        public UserTokenType TokenType => UserTokenType.IssuedToken;

        /// <inheritdoc/>
        public string? IssuedTokenProfileUri => m_options.ProfileUri;

        /// <inheritdoc/>
        public async ValueTask<AuthenticationResult> AuthenticateAsync(
            AuthenticationContext context,
            CancellationToken ct = default)
        {
            if (context.TokenHandler is not IssuedIdentityTokenHandler issuedTokenHandler ||
                !HandlesProfile(context, issuedTokenHandler))
            {
                return AuthenticationResult.NotHandled;
            }

            byte[]? tokenBytes = issuedTokenHandler.DecryptedTokenData;
            if (tokenBytes == null || tokenBytes.Length == 0)
            {
                return Reject(StatusCodes.BadIdentityTokenInvalid, "KeyCredential token data is empty.");
            }

            BridgeToken token;
            try
            {
                token = DecodeToken(tokenBytes);
            }
            catch (JsonException)
            {
                return Reject(StatusCodes.BadIdentityTokenInvalid, "KeyCredential token data is not valid JSON.");
            }
            catch (FormatException)
            {
                return Reject(StatusCodes.BadIdentityTokenInvalid, "KeyCredential proof is not valid base64url.");
            }

            if (string.IsNullOrWhiteSpace(token.CredentialId) ||
                string.IsNullOrWhiteSpace(token.Nonce) ||
                string.IsNullOrWhiteSpace(token.Proof))
            {
                return Reject(StatusCodes.BadIdentityTokenInvalid, "KeyCredential token is missing required fields.");
            }

            if (!IsFresh(token.IssuedAt))
            {
                return Reject(StatusCodes.BadIdentityTokenRejected, "KeyCredential proof nonce is expired.");
            }

            KeyCredential? credential = await m_store
                .GetAsync(token.CredentialId, ct)
                .ConfigureAwait(false);
            if (credential == null)
            {
                return Reject(StatusCodes.BadIdentityTokenRejected, "KeyCredential is not known to this server.");
            }

            if (credential.Expiration <= DateTime.UtcNow)
            {
                return Reject(StatusCodes.BadIdentityTokenRejected, "KeyCredential has expired.");
            }

            byte[] suppliedProof;
            try
            {
                suppliedProof = Base64UrlDecode(token.Proof);
            }
            catch (FormatException)
            {
                return Reject(StatusCodes.BadIdentityTokenInvalid, "KeyCredential proof is not valid base64url.");
            }

            byte[] expectedProof = ComputeProof(
                credential.Secret,
                token.CredentialId,
                token.Nonce,
                token.IssuedAt);

            if (!FixedTimeEquals(suppliedProof, expectedProof))
            {
                return Reject(StatusCodes.BadIdentityTokenRejected, "KeyCredential proof validation failed.");
            }

            IReadOnlyDictionary<string, object?> claims = BuildClaims(token.CredentialId, credential);
            return AuthenticationResult.Accept(new KeyCredentialUserIdentity(
                issuedTokenHandler,
                claims,
                ExtractStringList(claims, "groups"),
                ExtractStringList(claims, "roles"),
                GetOptionalString(claims, "iss"),
                GetOptionalString(claims, "sub") ?? token.CredentialId));
        }

        /// <summary>
        /// Creates a bridge-token JSON payload for a credential secret.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="secret"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="credentialId"/> or <paramref name="nonce"/> is <c>null</c>, empty, or whitespace.
        /// </exception>
        public static byte[] CreateTokenData(
            string credentialId,
            byte[] secret,
            string nonce,
            DateTime issuedAt)
        {
            if (string.IsNullOrWhiteSpace(credentialId))
            {
                throw new ArgumentException("CredentialId must be supplied.", nameof(credentialId));
            }
            if (secret == null)
            {
                throw new ArgumentNullException(nameof(secret));
            }
            if (string.IsNullOrWhiteSpace(nonce))
            {
                throw new ArgumentException("Nonce must be supplied.", nameof(nonce));
            }

            long issuedAtSeconds = new DateTimeOffset(DateTime.SpecifyKind(issuedAt, DateTimeKind.Utc))
                .ToUnixTimeSeconds();
            string proof = Base64UrlEncode(ComputeProof(secret, credentialId, nonce, issuedAtSeconds));
            string json = "{\"credentialId\":\"" +
                EscapeJson(credentialId) +
                "\",\"nonce\":\"" +
                EscapeJson(nonce) +
                "\",\"issuedAt\":" +
                issuedAtSeconds.ToString(CultureInfo.InvariantCulture) +
                ",\"proof\":\"" +
                proof +
                "\"}";
            return Encoding.UTF8.GetBytes(json);
        }

        /// <summary>
        /// Creates the base64url HMAC proof for a bridge token.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="secret"/> is <c>null</c>.</exception>
        public static string CreateProof(
            byte[] secret,
            string credentialId,
            string nonce,
            long issuedAt)
        {
            if (secret == null)
            {
                throw new ArgumentNullException(nameof(secret));
            }
            return Base64UrlEncode(ComputeProof(secret, credentialId, nonce, issuedAt));
        }

        private bool HandlesProfile(
            AuthenticationContext context,
            IssuedIdentityTokenHandler issuedTokenHandler)
        {
            return string.Equals(context.UserTokenPolicy.IssuedTokenType, m_options.ProfileUri, StringComparison.Ordinal) ||
                string.Equals(issuedTokenHandler.IssuedTokenTypeProfileUri, m_options.ProfileUri, StringComparison.Ordinal);
        }

        private bool IsFresh(long issuedAt)
        {
            if (issuedAt <= 0)
            {
                return false;
            }

            DateTime issuedAtUtc = DateTimeOffset.FromUnixTimeSeconds(issuedAt).UtcDateTime;
            TimeSpan age = DateTime.UtcNow - issuedAtUtc;
            return age >= TimeSpan.Zero && age <= m_options.NonceLifetime;
        }

        private static BridgeToken DecodeToken(byte[] tokenBytes)
        {
            using var document = JsonDocument.Parse(tokenBytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("KeyCredential token must be a JSON object.");
            }

            JsonElement root = document.RootElement;
            return new BridgeToken(
                GetString(root, "credentialId"),
                GetString(root, "nonce"),
                GetInt64(root, "issuedAt"),
                GetString(root, "proof"));
        }

        private static Dictionary<string, object?> BuildClaims(
            string credentialId,
            KeyCredential credential)
        {
            var claims = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> item in credential.Subject)
            {
                claims[item.Key] = item.Value;
            }
            if (!claims.ContainsKey("sub"))
            {
                claims["sub"] = credentialId;
            }
            if (credential.Scopes.Count > 0 && !claims.ContainsKey("scope"))
            {
                claims["scope"] = string.Join(" ", credential.Scopes);
            }
            return claims;
        }

        private static byte[] ComputeProof(
            byte[] secret,
            string credentialId,
            string nonce,
            long issuedAt)
        {
            string input = credentialId +
                "\n" +
                nonce +
                "\n" +
                issuedAt.ToString(CultureInfo.InvariantCulture);
            using var hmac = new HMACSHA256(secret);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            int difference = 0;
            for (int i = 0; i < left.Length; i++)
            {
                difference |= left[i] ^ right[i];
            }
            return difference == 0;
        }

        private static string GetString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement value) ||
                value.ValueKind != JsonValueKind.String)
            {
                return string.Empty;
            }
            return value.GetString() ?? string.Empty;
        }

        private static long GetInt64(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement value))
            {
                return 0;
            }
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long result)
                ? result
                : 0;
        }

        private static IReadOnlyList<string> ExtractStringList(
            IReadOnlyDictionary<string, object?> claims,
            string claimName)
        {
            if (!claims.TryGetValue(claimName, out object? value) || value == null)
            {
                return Array.Empty<string>();
            }

            if (value is string single)
            {
                return string.IsNullOrWhiteSpace(single)
                    ? Array.Empty<string>()
                    : [single];
            }

            if (value is IEnumerable<string> strings)
            {
                return new List<string>(strings).AsReadOnly();
            }

            return Array.Empty<string>();
        }

        private static string? GetOptionalString(
            IReadOnlyDictionary<string, object?> claims,
            string claimName)
        {
            return claims.TryGetValue(claimName, out object? value) ? value as string : null;
        }

        private static AuthenticationResult Reject(StatusCode statusCode, string message)
        {
            return AuthenticationResult.Reject(
                new ServiceResult(statusCode, new LocalizedText(message)));
        }

        private static byte[] Base64UrlDecode(string value)
        {
            string base64 = value.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2:
                    base64 += "==";
                    break;
                case 3:
                    base64 += "=";
                    break;
            }
            return Convert.FromBase64String(base64);
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string EscapeJson(string value)
        {
#pragma warning disable CA1307 // StringComparison overload is not available on every supported TFM.
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
#pragma warning restore CA1307
        }

        private sealed record BridgeToken(
            string CredentialId,
            string Nonce,
            long IssuedAt,
            string Proof);

        private sealed class KeyCredentialUserIdentity : IUserIdentity, IIdentityClaims
        {
            public KeyCredentialUserIdentity(
                IssuedIdentityTokenHandler tokenHandler,
                IReadOnlyDictionary<string, object?> claims,
                IReadOnlyList<string> groups,
                IReadOnlyList<string> roles,
                string? issuer,
                string? subject)
            {
                TokenHandler = tokenHandler ?? throw new ArgumentNullException(nameof(tokenHandler));
                Claims = claims ?? throw new ArgumentNullException(nameof(claims));
                Groups = groups ?? throw new ArgumentNullException(nameof(groups));
                Roles = roles ?? throw new ArgumentNullException(nameof(roles));
                Issuer = issuer;
                Subject = subject;
            }

            public IReadOnlyDictionary<string, object?> Claims { get; }

            public IReadOnlyList<string> Groups { get; }

            public IReadOnlyList<string> Roles { get; }

            public string? Issuer { get; }

            public string? Subject { get; }

            public string DisplayName => Subject ?? TokenHandler.DisplayName;

            public string PolicyId => TokenHandler.Token.PolicyId ?? string.Empty;

            public UserTokenType TokenType => UserTokenType.IssuedToken;

            public XmlQualifiedName IssuedTokenType => new(string.Empty, TokenHandler.IssuedTokenTypeProfileUri ?? string.Empty);

            public bool SupportsSignatures => false;

            public ArrayOf<NodeId> GrantedRoleIds => [];

            public IssuedIdentityTokenHandler TokenHandler { get; }

            IUserIdentityTokenHandler IUserIdentity.TokenHandler => TokenHandler;
        }
    }
}
