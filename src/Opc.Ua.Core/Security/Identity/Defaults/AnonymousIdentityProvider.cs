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
    /// Client identity provider for anonymous activation.
    /// </summary>
    public sealed class AnonymousIdentityProvider : IClientIdentityProvider
    {
        /// <inheritdoc/>
        public IReadOnlyList<UserTokenType> SupportedTokenTypes { get; }
            = [UserTokenType.Anonymous];

        /// <inheritdoc/>
        public IReadOnlyList<string> SupportedIssuedTokenProfileUris { get; }
            = [];

        /// <inheritdoc/>
        public DateTime ExpiresAt => DateTime.MaxValue;

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
            if (policy.TokenType != UserTokenType.Anonymous)
            {
                return new ValueTask<CanSatisfyResult>(
                    CanSatisfyResult.No(
                        $"TokenTypeNotSupported (provider handles Anonymous, policy is {policy.TokenType})."));
            }
            return new ValueTask<CanSatisfyResult>(CanSatisfyResult.Yes);
        }

        /// <inheritdoc/>
        public async ValueTask<IUserIdentity> GetIdentityAsync(
            UserTokenPolicy policy,
            IdentitySelectionContext context,
            CancellationToken ct = default)
        {
            CanSatisfyResult result = await CanSatisfyAsync(policy, context, ct)
                .ConfigureAwait(false);
            if (!result.CanSatisfy)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenRejected,
                    "Anonymous identity provider cannot satisfy the supplied user token policy: {0}",
                    result.RejectionReason ?? string.Empty);
            }

            return new UserIdentity
            {
                PolicyId = policy.PolicyId ?? string.Empty
            };
        }
    }
}
