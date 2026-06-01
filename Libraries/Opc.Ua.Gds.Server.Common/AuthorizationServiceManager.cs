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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Gds.Server.Identity;
using Opc.Ua.Identity;

#nullable enable

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// AuthorizationService coordinator used by the GDS node manager and
    /// hosted-service wiring.
    /// </summary>
    public sealed class AuthorizationServiceManager : IAccessTokenProvider
    {
        private static readonly char[] s_scopeSeparators = [' ', ',', ';', '\r', '\n', '\t'];

        private readonly InMemoryAccessTokenProvider m_provider;
        private readonly ITokenIssuer m_tokenIssuer;
        private readonly AuthorizationServiceOptions m_options;

        /// <summary>
        /// Creates an AuthorizationService manager.
        /// </summary>
        public AuthorizationServiceManager(
            InMemoryAccessTokenProvider provider,
            ITokenIssuer tokenIssuer,
            IOptions<AuthorizationServiceOptions> options)
        {
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            m_tokenIssuer = tokenIssuer ?? throw new ArgumentNullException(nameof(tokenIssuer));
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            m_options = options.Value ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Creates an AuthorizationService manager for tests and direct hosting.
        /// </summary>
        public AuthorizationServiceManager(
            InMemoryAccessTokenProvider provider,
            ITokenIssuer tokenIssuer,
            AuthorizationServiceOptions options)
            : this(provider, tokenIssuer, Options.Create(options))
        {
        }

        /// <summary>
        /// Supplies hosted-GDS certificate defaults to the default issuer.
        /// </summary>
        public void Initialize(ApplicationConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (m_tokenIssuer is CertificateJwtIssuer issuer &&
                configuration.CertificateManager?.CertificateProvider != null)
            {
                issuer.Initialize(
                    configuration.CertificateManager.CertificateProvider,
                    configuration.SecurityConfiguration?.ApplicationCertificate,
                    configuration.ApplicationUri);
            }
        }

        /// <inheritdoc/>
        [Obsolete("Use StartRequestTokenAsync + FinishRequestTokenAsync for Part 12 v1.05 compliance.")]
        public ValueTask<string> RequestAccessTokenAsync(
            UserIdentityToken identityToken,
            string resourceId,
            CancellationToken ct = default)
        {
#pragma warning disable CS0618 // Legacy Part 12 method remains functional for compatibility.
            return m_provider.RequestAccessTokenAsync(identityToken, resourceId, ct);
#pragma warning restore CS0618
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
        /// Begins the two-phase flow with access to the calling session's
        /// identity.
        /// </summary>
        public ValueTask<(ByteString serviceData, Guid requestId)> StartRequestTokenAsync(
            string resourceId,
            string policyId,
            ByteString requestorData,
            IUserIdentity? callerIdentity,
            CancellationToken ct = default)
        {
            string[] scopes = DecodeScopes(requestorData);
            if (scopes.Length == 0)
            {
                scopes = [.. m_options.DefaultScopes
                    .Where(scope => !string.IsNullOrWhiteSpace(scope))
                    .Distinct(StringComparer.Ordinal)];
            }

            ValidateAccess(callerIdentity, resourceId, scopes);
            return m_provider.StartRequestTokenAsync(
                resourceId,
                policyId,
                requestorData,
                callerIdentity,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<AccessTokenResult> FinishRequestTokenAsync(
            Guid requestId,
            ArrayOf<string> requestedRoles,
            UserIdentityToken userIdentityToken,
            SignatureData userTokenSignature,
            CancellationToken ct = default)
        {
            return m_provider.FinishRequestTokenAsync(
                requestId,
                requestedRoles,
                userIdentityToken,
                userTokenSignature,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<AccessTokenResult> RefreshTokenAsync(
            string resourceId,
            string currentRefreshToken,
            CancellationToken ct = default)
        {
            return m_provider.RefreshTokenAsync(resourceId, currentRefreshToken, ct);
        }

        private void ValidateAccess(
            IUserIdentity? callerIdentity,
            string audience,
            IReadOnlyList<string> scopes)
        {
            if (m_options.AllowedAudiences.Count != 0 &&
                !m_options.AllowedAudiences.Contains(audience, StringComparer.Ordinal))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUserAccessDenied,
                    "AuthorizationService audience is not allowed.");
            }

            if (m_options.AccessControl != null &&
                !m_options.AccessControl(callerIdentity, audience, scopes))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUserAccessDenied,
                    "AuthorizationService access control denied the token request.");
            }
        }

        private static string[] DecodeScopes(ByteString requestorData)
        {
            if (requestorData.Length == 0)
            {
                return [];
            }

            string text = System.Text.Encoding.UTF8.GetString(requestorData.ToArray());
            return [.. text
                .Split(s_scopeSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Where(scope => !string.IsNullOrWhiteSpace(scope))
                .Distinct(StringComparer.Ordinal)];
        }
    }
}
