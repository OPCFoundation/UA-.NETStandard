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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Identity;

#nullable enable

namespace Opc.Ua.Gds.Server.Identity
{
    /// <summary>
    /// In-memory AuthorizationService provider that delegates token
    /// signing to an <see cref="ITokenIssuer"/>.
    /// </summary>
    public sealed class InMemoryAccessTokenProvider : IAccessTokenProvider
    {
        private static readonly char[] s_scopeSeparators = [' ', ',', ';', '\r', '\n', '\t'];

        private readonly ITokenIssuer m_tokenIssuer;
        private readonly AuthorizationServiceOptions m_options;
        private readonly ConcurrentDictionary<Guid, RequestRecord> m_requests = new();

        /// <summary>
        /// Creates an in-memory token provider.
        /// </summary>
        public InMemoryAccessTokenProvider(
            ITokenIssuer tokenIssuer,
            IOptions<AuthorizationServiceOptions> options)
        {
            m_tokenIssuer = tokenIssuer ?? throw new ArgumentNullException(nameof(tokenIssuer));
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            m_options = options.Value ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Creates an in-memory token provider for tests and direct hosting.
        /// </summary>
        public InMemoryAccessTokenProvider(
            ITokenIssuer tokenIssuer,
            AuthorizationServiceOptions options)
            : this(tokenIssuer, Options.Create(options))
        {
        }

        /// <summary>
        /// Gets the signer used by this provider.
        /// </summary>
        internal ITokenIssuer TokenIssuer => m_tokenIssuer;

        /// <inheritdoc/>
        [Obsolete("Use StartRequestTokenAsync + FinishRequestTokenAsync for Part 12 v1.05 compliance.")]
        public async ValueTask<string> RequestAccessTokenAsync(
            UserIdentityToken identityToken,
            string resourceId,
            CancellationToken ct = default)
        {
            ValidateAudience(resourceId);
            string[] scopes = NormalizeScopes(m_options.DefaultScopes);
            AccessToken token = await IssueAsync(
                GetSubject(identityToken, null),
                resourceId,
                scopes,
                Array.Empty<string>(),
                ct)
                .ConfigureAwait(false);
            using (token)
            {
                return Encoding.UTF8.GetString(token.TokenData.ToArray());
            }
        }

        /// <inheritdoc/>
        public ValueTask<(ByteString serviceData, Guid requestId)> StartRequestTokenAsync(
            string resourceId,
            string policyId,
            ByteString requestorData,
            CancellationToken ct = default)
        {
            return StartRequestTokenAsync(resourceId, policyId, requestorData, null, ct);
        }

        /// <summary>
        /// Begins a token request for the supplied session identity.
        /// </summary>
        public ValueTask<(ByteString serviceData, Guid requestId)> StartRequestTokenAsync(
            string resourceId,
            string policyId,
            ByteString requestorData,
            IUserIdentity? callerIdentity,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ValidateAudience(resourceId);
            string[] scopes = DecodeScopes(requestorData);
            if (scopes.Length == 0)
            {
                scopes = NormalizeScopes(m_options.DefaultScopes);
            }

            Guid requestId = Guid.NewGuid();
            m_requests[requestId] = new RequestRecord(
                resourceId,
                policyId ?? string.Empty,
                scopes,
                callerIdentity?.DisplayName ?? string.Empty,
                DateTime.UtcNow.AddMinutes(5));

            return new ValueTask<(ByteString serviceData, Guid requestId)>((ByteString.Empty, requestId));
        }

        /// <inheritdoc/>
        public async ValueTask<AccessTokenResult> FinishRequestTokenAsync(
            Guid requestId,
            ArrayOf<string> requestedRoles,
            UserIdentityToken userIdentityToken,
            SignatureData userTokenSignature,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!m_requests.TryRemove(requestId, out RequestRecord? request))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "Unknown or expired AuthorizationService token request id.");
            }

            if (request.ExpiresAtUtc < DateTime.UtcNow)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTimeout,
                    "AuthorizationService token request expired.");
            }

            string[] roles = requestedRoles.IsEmpty
                ? Array.Empty<string>()
                : requestedRoles.ToArray()!
                    .Where(role => !string.IsNullOrWhiteSpace(role))
                    .ToArray();
            AccessToken token = await IssueAsync(
                GetSubject(userIdentityToken, request.Subject),
                request.ResourceId,
                request.Scopes,
                roles,
                ct)
                .ConfigureAwait(false);
            using (token)
            {
                return new AccessTokenResult
                {
                    AccessToken = Encoding.UTF8.GetString(token.TokenData.ToArray()),
                    AccessTokenExpiryTime = token.ExpiresAt,
                    RefreshToken = null,
                    RefreshTokenExpiryTime = DateTime.MinValue,
                    TokenType = "JWT",
                    PolicyId = string.IsNullOrEmpty(request.PolicyId) ? "jwt" : request.PolicyId,
                    AccessTokenBytes = token.TokenData.ToArray()
                };
            }
        }

        /// <inheritdoc/>
        public ValueTask<AccessTokenResult> RefreshTokenAsync(
            string resourceId,
            string currentRefreshToken,
            CancellationToken ct = default)
        {
            throw ServiceResultException.Create(
                StatusCodes.BadNotSupported,
                "AuthorizationService refresh tokens are not supported by the default in-memory provider.");
        }

        private ValueTask<AccessToken> IssueAsync(
            string subject,
            string audience,
            string[] scopes,
            string[] roles,
            CancellationToken ct)
        {
            var additionalClaims = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (roles.Length != 0)
            {
                additionalClaims["roles"] = roles;
            }

            return m_tokenIssuer.IssueAsync(
                new TokenIssuanceRequest(
                    subject,
                    audience,
                    scopes,
                    additionalClaims,
                    m_options.DefaultTokenLifetime),
                ct);
        }

        private void ValidateAudience(string resourceId)
        {
            if (string.IsNullOrEmpty(resourceId))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "AuthorizationService resource id must be supplied.");
            }

            if (m_options.AllowedAudiences.Count != 0 &&
                !m_options.AllowedAudiences.Contains(resourceId, StringComparer.Ordinal))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUserAccessDenied,
                    "AuthorizationService audience is not allowed.");
            }
        }

        private static string GetSubject(UserIdentityToken? identityToken, string? fallback)
        {
            if (identityToken is UserNameIdentityToken userName && !string.IsNullOrEmpty(userName.UserName))
            {
                return userName.UserName;
            }
            if (identityToken is X509IdentityToken x509 && x509.CertificateData.Length != 0)
            {
                return Convert.ToBase64String(x509.CertificateData.ToArray());
            }
            if (!string.IsNullOrEmpty(fallback))
            {
                return fallback!;
            }
            if (!string.IsNullOrEmpty(identityToken?.PolicyId))
            {
                return identityToken!.PolicyId!;
            }
            return "anonymous";
        }

        private static string[] DecodeScopes(ByteString requestorData)
        {
            if (requestorData.Length == 0)
            {
                return Array.Empty<string>();
            }

            string text = Encoding.UTF8.GetString(requestorData.ToArray());
            return text
                .Split(s_scopeSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Where(scope => !string.IsNullOrWhiteSpace(scope))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] NormalizeScopes(IEnumerable<string> scopes)
        {
            return scopes
                .Where(scope => !string.IsNullOrWhiteSpace(scope))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private sealed record RequestRecord(
            string ResourceId,
            string PolicyId,
            string[] Scopes,
            string Subject,
            DateTime ExpiresAtUtc);
    }
}
