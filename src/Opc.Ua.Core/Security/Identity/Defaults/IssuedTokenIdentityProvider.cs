// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Identity
{
    /// <summary>
    /// Client identity provider that acquires an issued access token for
    /// OPC UA issued-token policies, including explicitly selected vendor profiles.
    /// </summary>
    public sealed class IssuedTokenIdentityProvider : IClientIdentityProvider
    {
        /// <summary>
        /// Creates an issued-token identity provider.
        /// </summary>
        public IssuedTokenIdentityProvider(
            IAccessTokenProvider accessTokenProvider,
            string profileUri = Profiles.JwtUserToken)
        {
            m_accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
            m_profileUri = profileUri ?? throw new ArgumentNullException(nameof(profileUri));
        }

        /// <inheritdoc/>
        public IReadOnlyList<UserTokenType> SupportedTokenTypes { get; }
            = [UserTokenType.IssuedToken];

        /// <inheritdoc/>
        public IReadOnlyList<string> SupportedIssuedTokenProfileUris
            => [m_profileUri];

        /// <inheritdoc/>
        public DateTime ExpiresAt
        {
            get
            {
                lock (m_lock)
                {
                    return m_expiresAt;
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask<CanSatisfyResult> CanSatisfyAsync(
            UserTokenPolicy policy,
            IdentitySelectionContext context,
            CancellationToken ct = default)
        {
            if (policy == null)
            {
                return new ValueTask<CanSatisfyResult>(
                    CanSatisfyResult.No("Policy is null."));
            }
            if (policy.TokenType != UserTokenType.IssuedToken)
            {
                return new ValueTask<CanSatisfyResult>(
                    CanSatisfyResult.No(
                        $"TokenTypeNotSupported (provider handles IssuedToken, policy is {policy.TokenType})."));
            }
            if (!string.Equals(policy.IssuedTokenType, m_profileUri, StringComparison.Ordinal))
            {
                return new ValueTask<CanSatisfyResult>(
                    CanSatisfyResult.No(
                        $"IssuedTokenProfileNotSupported (provider handles '{m_profileUri}', policy requests '{policy.IssuedTokenType}')."));
            }
            return new ValueTask<CanSatisfyResult>(CanSatisfyResult.Yes);
        }

        /// <inheritdoc/>
        public async ValueTask<IUserIdentity> GetIdentityAsync(
            UserTokenPolicy policy,
            IdentitySelectionContext context,
            CancellationToken ct = default)
        {
            CanSatisfyResult satisfied = await CanSatisfyAsync(policy, context, ct)
                .ConfigureAwait(false);
            if (!satisfied.CanSatisfy)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenRejected,
                    "Issued-token identity provider cannot satisfy the supplied user token policy: {0}",
                    satisfied.RejectionReason ?? "token profile is not supported");
            }

            var metadata = AuthorizationServerMetadata.Parse(policy.IssuerEndpointUrl);
            AccessToken accessToken = m_accessTokenProvider is IEndpointAccessTokenProvider endpointProvider
                ? await endpointProvider.AcquireAsync(metadata, context.EndpointDescription, ct).ConfigureAwait(false)
                : await m_accessTokenProvider.AcquireAsync(metadata, ct).ConfigureAwait(false);
            try
            {
                byte[] tokenData = accessToken.TokenData.ToArray();
                var identity = new UserIdentity(
                    tokenData,
                    accessToken.ProfileUri,
                    context.SecurityPolicyRegistry)
                {
                    PolicyId = policy.PolicyId ?? string.Empty
                };

                lock (m_lock)
                {
                    m_expiresAt = accessToken.ExpiresAt;
                }

                return identity;
            }
            finally
            {
                accessToken.Dispose();
            }
        }

        private readonly IAccessTokenProvider m_accessTokenProvider;
        private readonly string m_profileUri;
        private readonly Lock m_lock = new();
        private DateTime m_expiresAt = DateTime.MaxValue;
    }
}
