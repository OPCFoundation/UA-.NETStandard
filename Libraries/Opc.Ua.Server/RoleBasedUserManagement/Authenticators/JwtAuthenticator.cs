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
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Identity;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Validates JWT issued identity tokens without a JWT library dependency.
    /// </summary>
    public sealed class JwtAuthenticator : IUserTokenAuthenticator
    {
        private readonly IIssuerKeyResolver? m_keyResolver;
        private readonly string? m_expectedAudience;
        private readonly TimeSpan m_clockSkewTolerance;
        private readonly Func<IssuedIdentityTokenHandler, CancellationToken, ValueTask<IUserIdentity?>>? m_verify;

        /// <summary>
        /// Creates a JWT authenticator.
        /// </summary>
        public JwtAuthenticator(
            IIssuerKeyResolver keyResolver,
            string expectedAudience,
            TimeSpan? clockSkewTolerance = null)
        {
            m_keyResolver = keyResolver ?? throw new ArgumentNullException(nameof(keyResolver));
            if (string.IsNullOrEmpty(expectedAudience))
            {
                throw new ArgumentException(
                    "Expected audience must be supplied.",
                    nameof(expectedAudience));
            }
            m_expectedAudience = expectedAudience;
            m_clockSkewTolerance = clockSkewTolerance ?? TimeSpan.FromSeconds(60);
        }

        /// <summary>
        /// Creates a delegate-backed JWT authenticator.
        /// </summary>
        public JwtAuthenticator(
            Func<IssuedIdentityTokenHandler, CancellationToken, ValueTask<IUserIdentity?>> verify)
        {
            m_verify = verify ?? throw new ArgumentNullException(nameof(verify));
            m_clockSkewTolerance = TimeSpan.FromSeconds(60);
        }

        /// <inheritdoc/>
        public UserTokenType TokenType => UserTokenType.IssuedToken;

        /// <inheritdoc/>
        public string? IssuedTokenProfileUri => Profiles.JwtUserToken;

        /// <inheritdoc/>
        public async ValueTask<AuthenticationResult> AuthenticateAsync(
            AuthenticationContext context,
            CancellationToken ct = default)
        {
            if (context.TokenHandler is not IssuedIdentityTokenHandler issuedTokenHandler ||
                issuedTokenHandler.IssuedTokenType != IssuedTokenType.JWT)
            {
                return AuthenticationResult.NotHandled;
            }

            if (m_verify != null)
            {
                return await AuthenticateWithVerifierAsync(issuedTokenHandler, ct).ConfigureAwait(false);
            }

            byte[]? tokenBytes = issuedTokenHandler.DecryptedTokenData;
            if (tokenBytes == null || tokenBytes.Length == 0)
            {
                return Reject(StatusCodes.BadIdentityTokenInvalid, "JWT token data is empty.");
            }

            string jwt = Encoding.UTF8.GetString(tokenBytes, 0, tokenBytes.Length);
            string[] segments = jwt.Split('.');
            if (segments.Length != 3 ||
                string.IsNullOrEmpty(segments[0]) ||
                string.IsNullOrEmpty(segments[1]) ||
                string.IsNullOrEmpty(segments[2]))
            {
                return Reject(StatusCodes.BadIdentityTokenInvalid, "JWT must contain header, payload and signature.");
            }

            byte[] headerBytes;
            byte[] payloadBytes;
            byte[] signatureBytes;
            try
            {
                headerBytes = Base64UrlDecode(segments[0]);
                payloadBytes = Base64UrlDecode(segments[1]);
                signatureBytes = Base64UrlDecode(segments[2]);
            }
            catch (FormatException)
            {
                return Reject(StatusCodes.BadIdentityTokenInvalid, "JWT contains invalid base64url data.");
            }

            string? keyId;
            string? algorithm;
            try
            {
                using var headerDocument = JsonDocument.Parse(headerBytes);
                JsonElement header = headerDocument.RootElement;
                keyId = GetOptionalString(header, "kid");
                algorithm = GetOptionalString(header, "alg");
            }
            catch (JsonException)
            {
                return Reject(StatusCodes.BadIdentityTokenInvalid, "JWT header is not valid JSON.");
            }

            if (string.IsNullOrEmpty(algorithm))
            {
                return Reject(StatusCodes.BadIdentityTokenInvalid, "JWT header does not specify an algorithm.");
            }

            byte[] signingInputBytes = Encoding.ASCII.GetBytes(segments[0] + "." + segments[1]);
            IReadOnlyList<IssuerVerificationKey> keys = await m_keyResolver!
                .GetKeysAsync(keyId, ct)
                .ConfigureAwait(false);

            bool signatureValid = false;
            foreach (IssuerVerificationKey key in keys)
            {
                if (!string.Equals(key.Algorithm, algorithm, StringComparison.Ordinal))
                {
                    continue;
                }
                if (key.KeyId != null &&
                    keyId != null &&
                    !string.Equals(key.KeyId, keyId, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    if (key.VerifySignature(signingInputBytes, signatureBytes))
                    {
                        signatureValid = true;
                        break;
                    }
                }
                catch (NotSupportedException)
                {
                    return Reject(StatusCodes.BadIdentityTokenRejected, "JWT signing algorithm is not supported.");
                }
            }

            if (!signatureValid)
            {
                return Reject(StatusCodes.BadIdentityTokenRejected, "JWT signature validation failed.");
            }

            try
            {
                using var payloadDocument = JsonDocument.Parse(payloadBytes);
                JsonElement payload = payloadDocument.RootElement;
                ServiceResult? validationError = ValidatePayload(payload);
                if (ServiceResult.IsBad(validationError))
                {
                    return AuthenticationResult.Reject(validationError!);
                }

                IReadOnlyDictionary<string, object?> claims = ExtractClaims(payload);
                IReadOnlyList<string> groups = ExtractStringList(claims, "groups");
                IReadOnlyList<string> roles = ExtractStringList(claims, "roles");
                string? issuer = GetOptionalString(payload, "iss");
                string? subject = GetOptionalString(payload, "sub");

                return AuthenticationResult.Accept(
                    new JwtUserIdentity(
                        issuedTokenHandler,
                        claims,
                        groups,
                        roles,
                        issuer,
                        subject));
            }
            catch (JsonException)
            {
                return Reject(StatusCodes.BadIdentityTokenInvalid, "JWT payload is not valid JSON.");
            }
        }

        private async ValueTask<AuthenticationResult> AuthenticateWithVerifierAsync(
            IssuedIdentityTokenHandler issuedTokenHandler,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                IUserIdentity? identity = await m_verify!(issuedTokenHandler, ct).ConfigureAwait(false);
                return identity == null
                    ? Reject(
                        StatusCodes.BadIdentityTokenRejected,
                        "No token validator is configured.")
                    : AuthenticationResult.Accept(identity);
            }
            catch (ServiceResultException ex)
            {
                return AuthenticationResult.Reject(ex.Result);
            }
        }

        private ServiceResult? ValidatePayload(JsonElement payload)
        {
            string? issuer = GetOptionalString(payload, "iss");
            if (!string.Equals(issuer, m_keyResolver!.IssuerUri, StringComparison.Ordinal))
            {
                return CreateError(StatusCodes.BadIdentityTokenRejected, "JWT issuer is not trusted.");
            }

            if (!AudienceContainsExpected(payload))
            {
                return CreateError(StatusCodes.BadIdentityTokenRejected, "JWT audience does not match this server.");
            }

            if (!TryGetInt64(payload, "exp", out long expiresAt))
            {
                return CreateError(StatusCodes.BadIdentityTokenInvalid, "JWT expiration is missing or invalid.");
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long skewSeconds = (long)m_clockSkewTolerance.TotalSeconds;
            if (now > expiresAt + skewSeconds)
            {
                return CreateError(StatusCodes.BadIdentityTokenRejected, "JWT has expired.");
            }

            if (TryGetInt64(payload, "nbf", out long notBefore) && now + skewSeconds < notBefore)
            {
                return CreateError(StatusCodes.BadIdentityTokenRejected, "JWT is not valid yet.");
            }

            if (TryGetInt64(payload, "iat", out long issuedAt) && now + skewSeconds < issuedAt)
            {
                return CreateError(StatusCodes.BadIdentityTokenRejected, "JWT was issued in the future.");
            }

            return null;
        }

        private bool AudienceContainsExpected(JsonElement payload)
        {
            if (!payload.TryGetProperty("aud", out JsonElement audience))
            {
                return false;
            }

            if (audience.ValueKind == JsonValueKind.String)
            {
                return string.Equals(audience.GetString(), m_expectedAudience, StringComparison.Ordinal);
            }

            if (audience.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (JsonElement value in audience.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.String &&
                    string.Equals(value.GetString(), m_expectedAudience, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static Dictionary<string, object?> ExtractClaims(JsonElement payload)
        {
            var claims = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (JsonProperty property in payload.EnumerateObject())
            {
                AddClaim(claims, property.Name, property.Value);
            }
            return claims;
        }

        private static void AddClaim(
            IDictionary<string, object?> claims,
            string name,
            JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in value.EnumerateObject())
                    {
                        AddClaim(claims, name + "." + property.Name, property.Value);
                    }
                    break;
                case JsonValueKind.Array:
                    var values = new List<string>();
                    foreach (JsonElement element in value.EnumerateArray())
                    {
                        values.Add(GetClaimArrayValue(element));
                    }
                    claims[name] = values.AsReadOnly();
                    break;
                case JsonValueKind.String:
                    claims[name] = value.GetString();
                    break;
                case JsonValueKind.Number:
                    if (value.TryGetInt64(out long longValue))
                    {
                        claims[name] = longValue;
                    }
                    else
                    {
                        claims[name] = value.GetDouble();
                    }
                    break;
                case JsonValueKind.True:
                    claims[name] = true;
                    break;
                case JsonValueKind.False:
                    claims[name] = false;
                    break;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    claims[name] = null;
                    break;
            }
        }

        private static string GetClaimArrayValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Number => element.TryGetInt64(out long longValue)
                    ? longValue.ToString(CultureInfo.InvariantCulture)
                    : element.GetDouble().ToString(CultureInfo.InvariantCulture),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                _ => element.GetRawText()
            };
        }

        private static IReadOnlyList<string> ExtractStringList(
            IReadOnlyDictionary<string, object?> claims,
            string name)
        {
            if (!claims.TryGetValue(name, out object? value) || value == null)
            {
                return Array.Empty<string>();
            }

            if (value is IReadOnlyList<string> list)
            {
                return list;
            }

            if (value is string singleValue && singleValue.Length > 0)
            {
                return new[] { singleValue };
            }

            return Array.Empty<string>();
        }

        private static bool TryGetInt64(JsonElement payload, string propertyName, out long value)
        {
            value = 0;
            return payload.TryGetProperty(propertyName, out JsonElement property) &&
                property.ValueKind == JsonValueKind.Number &&
                property.TryGetInt64(out value);
        }

        private static string? GetOptionalString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement property) ||
                property.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return property.GetString();
        }

        private static byte[] Base64UrlDecode(string value)
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
            }

            return Convert.FromBase64String(padded);
        }

        private static AuthenticationResult Reject(StatusCode statusCode, string message)
        {
            return AuthenticationResult.Reject(CreateError(statusCode, message));
        }

        private static ServiceResult CreateError(StatusCode statusCode, string message)
        {
            return new ServiceResult(statusCode, new LocalizedText(message));
        }
    }
}
