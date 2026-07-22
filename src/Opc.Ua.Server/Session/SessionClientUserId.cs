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

namespace Opc.Ua.Server
{
    internal static class SessionClientUserId
    {
        public static string? Get(
            IUserIdentityTokenHandler identityToken,
            IUserIdentity authenticatedIdentity)
        {
            if (identityToken == null)
            {
                throw new ArgumentNullException(nameof(identityToken));
            }
            if (authenticatedIdentity == null)
            {
                throw new ArgumentNullException(nameof(authenticatedIdentity));
            }

            return identityToken.Token switch
            {
                AnonymousIdentityToken => null,
                UserNameIdentityToken userNameToken => userNameToken.UserName,
                X509IdentityToken x509Token => GetX509Subject(x509Token),
                IssuedIdentityToken => GetIssuedTokenOwner(identityToken, authenticatedIdentity),
                _ => throw new ServiceResultException(
                    StatusCodes.BadIdentityTokenInvalid,
                    "The UserIdentityToken type does not define a ClientUserId.")
            };
        }

        public static bool TryGet(
            IUserIdentityTokenHandler identityToken,
            IUserIdentity authenticatedIdentity,
            out string? clientUserId)
        {
            try
            {
                clientUserId = Get(identityToken, authenticatedIdentity);
                return true;
            }
            catch (ServiceResultException exception)
                when (exception.StatusCode == StatusCodes.BadIdentityTokenInvalid)
            {
                clientUserId = null;
                return false;
            }
        }

        private static string GetX509Subject(X509IdentityToken token)
        {
            using Certificate? certificate = token.CertificateData.IsEmpty
                ? null
                : Certificate.FromRawData(token.CertificateData);
            return certificate?.Subject ??
                throw new ServiceResultException(
                    StatusCodes.BadIdentityTokenInvalid,
                    "The X509IdentityToken does not contain a valid Certificate.");
        }

        private static string GetIssuedTokenOwner(
            IUserIdentityTokenHandler identityToken,
            IUserIdentity authenticatedIdentity)
        {
            while (authenticatedIdentity is RoleBasedIdentity roleBasedIdentity)
            {
                authenticatedIdentity = roleBasedIdentity.AuthenticatedIdentity;
            }

            if (authenticatedIdentity is IIdentityClaims claims &&
                TryGetClaimsOwner(claims, out string? issuer, out string? subject))
            {
                return GetIssuedTokenOwner(issuer, subject);
            }

            if (identityToken is IssuedIdentityTokenHandler issuedToken &&
                issuedToken.IssuedTokenType == IssuedTokenType.JWT &&
                TryGetJwtOwner(issuedToken, out issuer, out subject))
            {
                return GetIssuedTokenOwner(issuer, subject);
            }

            throw new ServiceResultException(
                StatusCodes.BadIdentityTokenInvalid,
                "The authenticated IssuedIdentityToken does not expose a stable owner.");
        }

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

        private static bool TryGetJwtOwner(
            IssuedIdentityTokenHandler token,
            out string? issuer,
            [NotNullWhen(true)] out string? subject)
        {
            issuer = null;
            subject = null;
            byte[]? tokenData = token.DecryptedTokenData;
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

        private static string GetIssuedTokenOwner(string? issuer, string subject)
        {
            return issuer == null ? subject : string.Concat(issuer, subject);
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
    }
}
