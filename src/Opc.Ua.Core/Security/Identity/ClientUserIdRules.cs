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
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// The token and claims rules that name the user of an identity: the ClientUserId of
    /// OPC 10000-5 6.4.3 (username, X.509 subject, or issued-token issuer and subject).
    /// </summary>
    internal static class ClientUserIdRules
    {
        /// <summary>
        /// Resolves the ClientUserId of an identity from the token handler it holds, or from
        /// its claims when it holds none.
        /// </summary>
        /// <param name="identity">The identity.</param>
        /// <param name="clientUserId">The ClientUserId; <c>null</c> for an anonymous user.</param>
        /// <returns>
        /// <c>false</c> when neither the token nor the claims define a ClientUserId.
        /// </returns>
        public static bool TryResolve(IUserIdentity identity, out string? clientUserId)
        {
            clientUserId = null;
            if (identity.TokenHandler is IUserIdentityTokenHandler identityToken)
            {
                try
                {
                    ResolveParts(identityToken, identity, out _, out string? issuer, out string? subject);
                    clientUserId = Combine(issuer, subject);
                    return true;
                }
                catch (ServiceResultException exception)
                    when (exception.StatusCode == StatusCodes.BadIdentityTokenInvalid)
                {
                    return false;
                }
            }

            if (identity is IIdentityClaims claims &&
                TryGetClaimsOwner(claims, out string? claimsIssuer, out string? claimsSubject))
            {
                clientUserId = Combine(claimsIssuer, claimsSubject);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Joins an issued-token issuer and subject into the ClientUserId.
        /// </summary>
        public static string? Combine(string? issuer, string? subject)
        {
            if (subject == null)
            {
                return null;
            }
            return issuer == null ? subject : string.Concat(issuer, subject);
        }

        /// <summary>
        /// Resolves the token type and the issuer and subject components that identify the
        /// authenticated owner. A <c>null</c> subject denotes an anonymous identity. The
        /// caller unwraps identities that decorate the authenticated one.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="identityToken"/> or <paramref name="authenticatedIdentity"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// The token type does not expose a valid ClientUserId.
        /// </exception>
        public static void ResolveParts(
            IUserIdentityTokenHandler identityToken,
            IUserIdentity authenticatedIdentity,
            out UserTokenType tokenType,
            out string? issuer,
            out string? subject)
        {
            if (identityToken == null)
            {
                throw new ArgumentNullException(nameof(identityToken));
            }
            if (authenticatedIdentity == null)
            {
                throw new ArgumentNullException(nameof(authenticatedIdentity));
            }

            issuer = null;
            switch (identityToken.Token)
            {
                case AnonymousIdentityToken:
                    tokenType = UserTokenType.Anonymous;
                    subject = null;
                    return;
                case UserNameIdentityToken userNameToken:
                    tokenType = UserTokenType.UserName;
                    subject = userNameToken.UserName;
                    return;
                case X509IdentityToken x509Token:
                    tokenType = UserTokenType.Certificate;
                    subject = GetX509Subject(x509Token);
                    return;
                case IssuedIdentityToken:
                    tokenType = UserTokenType.IssuedToken;
                    subject = GetIssuedTokenOwner(
                        identityToken,
                        authenticatedIdentity,
                        out issuer);
                    return;
                default:
                    throw new ServiceResultException(
                        StatusCodes.BadIdentityTokenInvalid,
                        "The UserIdentityToken type does not define a ClientUserId.");
            }
        }

        /// <summary>
        /// Resolves the certificate subject used as the X.509 ClientUserId.
        /// </summary>
        /// <exception cref="ServiceResultException">The token holds no valid Certificate.</exception>
        public static string GetX509Subject(X509IdentityToken token)
        {
            using Certificate? certificate = token.CertificateData.IsEmpty
                ? null
                : Certificate.FromRawData(token.CertificateData);
            return certificate?.Subject ??
                throw new ServiceResultException(
                    StatusCodes.BadIdentityTokenInvalid,
                    "The X509IdentityToken does not contain a valid Certificate.");
        }

        /// <summary>
        /// Resolves a stable issued-token owner from authenticated claims or validated JWT
        /// data, returning the issuer and subject separately so the caller can encode them
        /// without ambiguity.
        /// </summary>
        private static string GetIssuedTokenOwner(
            IUserIdentityTokenHandler identityToken,
            IUserIdentity authenticatedIdentity,
            out string? issuer)
        {
            if (authenticatedIdentity is IIdentityClaims claims &&
                TryGetClaimsOwner(claims, out issuer, out string? subject))
            {
                return subject;
            }

            if (identityToken is IssuedIdentityTokenHandler issuedToken &&
                issuedToken.IssuedTokenType == IssuedTokenType.JWT &&
                TryGetJwtOwner(issuedToken.DecryptedTokenData, out issuer, out subject))
            {
                return subject;
            }

            throw new ServiceResultException(
                StatusCodes.BadIdentityTokenInvalid,
                "The authenticated IssuedIdentityToken does not expose a stable owner.");
        }

        /// <summary>
        /// Attempts to read the issued-token issuer and subject from authenticated claims.
        /// </summary>
        private static bool TryGetClaimsOwner(
            IIdentityClaims claims,
            out string? issuer,
            [NotNullWhen(true)] out string? subject)
        {
            issuer = claims.Issuer;
            subject = claims.Subject;

            if (string.IsNullOrEmpty(subject) &&
                claims.Claims.TryGetValue("sub", out object? subjectClaim))
            {
                subject = subjectClaim as string;
            }
            if (issuer == null &&
                claims.Claims.TryGetValue("iss", out object? issuerClaim))
            {
                issuer = issuerClaim as string;
            }

            return !string.IsNullOrEmpty(subject);
        }

        /// <summary>
        /// Attempts to read the issuer and subject from a JWT token payload. The token data
        /// is owned by the method and cleared before it returns.
        /// </summary>
        public static bool TryGetJwtOwner(
            byte[]? tokenData,
            out string? issuer,
            [NotNullWhen(true)] out string? subject)
        {
            issuer = null;
            subject = null;
            byte[]? payloadData = null;

            try
            {
                if (tokenData == null || tokenData.Length == 0)
                {
                    return false;
                }

                string jwt = Encoding.UTF8.GetString(tokenData);
                int firstSeparator = -1;
                int secondSeparator = -1;
                for (int index = 0; index < jwt.Length; index++)
                {
                    if (jwt[index] != '.')
                    {
                        continue;
                    }
                    if (firstSeparator < 0)
                    {
                        firstSeparator = index;
                    }
                    else
                    {
                        secondSeparator = index;
                        break;
                    }
                }
                if (firstSeparator <= 0 ||
                    secondSeparator <= firstSeparator + 1 ||
                    secondSeparator == jwt.Length - 1)
                {
                    return false;
                }

                payloadData = Base64UrlDecode(
                    jwt.Substring(
                        firstSeparator + 1,
                        secondSeparator - firstSeparator - 1));
                using JsonDocument document = JsonDocument.Parse(payloadData);
                JsonElement payload = document.RootElement;
                if (payload.ValueKind != JsonValueKind.Object ||
                    !payload.TryGetProperty("sub", out JsonElement subjectProperty) ||
                    subjectProperty.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                subject = subjectProperty.GetString();
                if (string.IsNullOrEmpty(subject))
                {
                    return false;
                }

                if (payload.TryGetProperty("iss", out JsonElement issuerProperty))
                {
                    if (issuerProperty.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }
                    issuer = issuerProperty.GetString();
                }

                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
            finally
            {
                if (tokenData != null)
                {
                    Array.Clear(tokenData, 0, tokenData.Length);
                }
                if (payloadData != null)
                {
                    Array.Clear(payloadData, 0, payloadData.Length);
                }
            }
        }

        /// <summary>
        /// Decodes an unpadded Base64Url value.
        /// </summary>
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
    }
}
