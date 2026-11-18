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
using System.Globalization;
using System.Security.Cryptography;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Resolves the OPC UA ClientUserId of an authenticated Session owner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The resolver uses the token-specific identity defined by OPC 10000-4:
    /// the username, X.509 subject, or stable issued-token issuer and subject.
    /// </para>
    /// <para>
    /// The value has two distinct roles that must not share one representation.
    /// <see cref="Resolve"/> produces the human readable identifier that
    /// OPC 10000-5 exposes through SessionSecurityDiagnostics, while
    /// <see cref="ResolveContinuityKey"/> produces the key used for the identity
    /// continuity checks required by OPC 10000-4 5.7.3.1. The continuity key
    /// encodes the token type and the issuer length so that two different
    /// identities can never resolve to the same key.
    /// </para>
    /// </remarks>
    internal static class ClientUserIdResolver
    {
        /// <summary>
        /// Resolves the human readable ClientUserId reported in the Session
        /// security diagnostics (OPC 10000-5).
        /// </summary>
        /// <remarks>
        /// The result is intended for diagnostics only. Use
        /// <see cref="ResolveContinuityKey"/> to compare Session owners.
        /// </remarks>
        /// <param name="identityToken">The validated identity token handler.</param>
        /// <param name="authenticatedIdentity">The authenticated identity produced for the token.</param>
        /// <returns>
        /// The ClientUserId, or <c>null</c> for an anonymous identity.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="identityToken"/> or <paramref name="authenticatedIdentity"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// The token type does not expose a valid ClientUserId.
        /// </exception>
        public static string? Resolve(
            IUserIdentityTokenHandler identityToken,
            IUserIdentity authenticatedIdentity)
        {
            ResolveParts(
                identityToken,
                authenticatedIdentity,
                out _,
                out string? issuer,
                out string? subject);
            if (subject == null)
            {
                return null;
            }
            return issuer == null ? subject : string.Concat(issuer, subject);
        }

        /// <summary>
        /// Attempts to resolve the human readable ClientUserId reported in the
        /// Session security diagnostics.
        /// </summary>
        /// <param name="identityToken">The validated identity token handler.</param>
        /// <param name="authenticatedIdentity">The authenticated identity produced for the token.</param>
        /// <param name="clientUserId">
        /// The resolved ClientUserId, or <c>null</c> for anonymous and invalid identities.
        /// </param>
        /// <returns>
        /// <c>true</c> when the token type defines a ClientUserId; otherwise, <c>false</c>.
        /// </returns>
        public static bool TryResolve(
            IUserIdentityTokenHandler identityToken,
            IUserIdentity authenticatedIdentity,
            out string? clientUserId)
        {
            try
            {
                clientUserId = Resolve(identityToken, authenticatedIdentity);
                return true;
            }
            catch (ServiceResultException exception)
                when (exception.StatusCode == StatusCodes.BadIdentityTokenInvalid)
            {
                clientUserId = null;
                return false;
            }
        }

        /// <summary>
        /// Resolves the ClientUserId of the user that activated a Session, as
        /// reported in audit events (OPC 10000-5 6.4.3).
        /// </summary>
        /// <param name="session">The Session, or <c>null</c>.</param>
        /// <returns>
        /// The ClientUserId, or <c>null</c> for an anonymous, unknown or invalid identity.
        /// </returns>
        public static string? ResolveSession(ISession? session)
        {
            if (session is Session serverSession)
            {
                return serverSession.ClientUserId;
            }
            if (session?.IdentityToken == null || session.Identity == null)
            {
                return null;
            }
            TryResolve(session.IdentityToken, session.Identity, out string? clientUserId);
            return clientUserId;
        }

        /// <summary>
        /// Resolves the ClientUserId of an authenticated identity from the token
        /// handler it holds, as reported in audit events (OPC 10000-5 6.4.3).
        /// </summary>
        /// <param name="identity">The authenticated identity, or <c>null</c>.</param>
        /// <returns>
        /// The ClientUserId, or <c>null</c> for an anonymous, unknown or invalid identity.
        /// </returns>
        public static string? ResolveIdentity(IUserIdentity? identity)
        {
            if (identity?.TokenHandler is not IUserIdentityTokenHandler identityToken)
            {
                return null;
            }
            TryResolve(identityToken, identity, out string? clientUserId);
            return clientUserId;
        }

        /// <summary>
        /// Resolves the ClientUserId of the token a Client supplied in an
        /// ActivateSession request that was not authenticated, as reported in the
        /// failed AuditActivateSessionEvent (OPC 10000-5 6.4.3).
        /// </summary>
        /// <remarks>
        /// An issued token resolves only when it was sent unencrypted as a JWT; the
        /// token is not validated, so the result names the claimed user.
        /// </remarks>
        /// <param name="identityToken">The requested token; <c>null</c> requests an anonymous user.</param>
        /// <returns>
        /// The ClientUserId, or <c>null</c> for an anonymous token or one without a
        /// readable user.
        /// </returns>
        public static string? ResolveRequested(UserIdentityToken? identityToken)
        {
            try
            {
                switch (identityToken)
                {
                    case UserNameIdentityToken userNameToken:
                        return userNameToken.UserName;
                    case X509IdentityToken x509Token:
                        return GetX509Subject(x509Token);
                    case IssuedIdentityToken issuedToken
                        when string.IsNullOrEmpty(issuedToken.EncryptionAlgorithm) &&
                            !issuedToken.TokenData.IsEmpty:
                        return TryGetJwtOwner(
                            issuedToken.TokenData.ToArray(),
                            out string? issuer,
                            out string? subject)
                            ? issuer == null ? subject : string.Concat(issuer, subject)
                            : null;
                    default:
                        return null;
                }
            }
            catch (Exception exception) when (
                exception is ServiceResultException or CryptographicException or FormatException)
            {
                return null;
            }
        }

        /// <summary>
        /// Resolves the key used to verify that a Session keeps the same
        /// ClientUserId across a SecureChannel or Subscription transfer
        /// (OPC 10000-4 5.7.3.1).
        /// </summary>
        /// <remarks>
        /// The key encodes the user token type and the length of the issuer so
        /// that neither a different token type carrying the same identifier nor a
        /// different issuer and subject split can produce an equal key.
        /// </remarks>
        /// <param name="identityToken">The validated identity token handler.</param>
        /// <param name="authenticatedIdentity">The authenticated identity produced for the token.</param>
        /// <returns>
        /// The continuity key, or <c>null</c> for an anonymous identity.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="identityToken"/> or <paramref name="authenticatedIdentity"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// The token type does not expose a valid ClientUserId.
        /// </exception>
        public static string? ResolveContinuityKey(
            IUserIdentityTokenHandler identityToken,
            IUserIdentity authenticatedIdentity)
        {
            ResolveParts(
                identityToken,
                authenticatedIdentity,
                out UserTokenType tokenType,
                out string? issuer,
                out string? subject);
            if (subject == null)
            {
                return null;
            }

            return string.Concat(
                ((int)tokenType).ToString(CultureInfo.InvariantCulture),
                ":",
                (issuer?.Length ?? -1).ToString(CultureInfo.InvariantCulture),
                ":",
                issuer,
                subject);
        }

        /// <summary>
        /// Attempts to resolve the identity continuity key of a validated
        /// identity token.
        /// </summary>
        /// <param name="identityToken">The validated identity token handler.</param>
        /// <param name="authenticatedIdentity">The authenticated identity produced for the token.</param>
        /// <param name="continuityKey">
        /// The resolved continuity key, or <c>null</c> for anonymous and invalid identities.
        /// </param>
        /// <returns>
        /// <c>true</c> when the token type defines a ClientUserId; otherwise, <c>false</c>.
        /// </returns>
        public static bool TryResolveContinuityKey(
            IUserIdentityTokenHandler identityToken,
            IUserIdentity authenticatedIdentity,
            out string? continuityKey)
        {
            try
            {
                continuityKey = ResolveContinuityKey(identityToken, authenticatedIdentity);
                return true;
            }
            catch (ServiceResultException exception)
                when (exception.StatusCode == StatusCodes.BadIdentityTokenInvalid)
            {
                continuityKey = null;
                return false;
            }
        }

        /// <summary>
        /// Resolves the token type and the issuer and subject components that
        /// identify the authenticated owner, looking through Role decorations to
        /// the authenticated identity. A <c>null</c> subject denotes an anonymous identity.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="identityToken"/> or <paramref name="authenticatedIdentity"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// The token type does not expose a valid ClientUserId.
        /// </exception>
        private static void ResolveParts(
            IUserIdentityTokenHandler identityToken,
            IUserIdentity authenticatedIdentity,
            out UserTokenType tokenType,
            out string? issuer,
            out string? subject)
        {
            ClientUserIdRules.ResolveParts(
                identityToken,
                authenticatedIdentity,
                out tokenType,
                out issuer,
                out subject);
        }

        /// <summary>
        /// Resolves the certificate subject used as the X.509 ClientUserId.
        /// </summary>
        private static string GetX509Subject(X509IdentityToken token)
        {
            return ClientUserIdRules.GetX509Subject(token);
        }

        /// <summary>
        /// Attempts to read the issuer and subject from a JWT token payload. The
        /// token data is owned by the method and cleared before it returns.
        /// </summary>
        private static bool TryGetJwtOwner(
            byte[]? tokenData,
            out string? issuer,
            [NotNullWhen(true)] out string? subject)
        {
            return ClientUserIdRules.TryGetJwtOwner(tokenData, out issuer, out subject);
        }
    }
}
