// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Identity
{
    /// <summary>
    /// Client identity provider that resolves a username password from an
    /// <see cref="ISecretRegistry"/> for each activation.
    /// </summary>
    public sealed class UserNamePasswordIdentityProvider : IClientIdentityProvider
    {
        /// <summary>
        /// Creates a username/password provider.
        /// </summary>
        public UserNamePasswordIdentityProvider(
            string username,
            ISecretRegistry registry,
            SecretIdentifier passwordId)
        {
            m_username = username ?? throw new ArgumentNullException(nameof(username));
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_passwordId = passwordId ?? throw new ArgumentNullException(nameof(passwordId));
        }

        /// <inheritdoc/>
        public IReadOnlyList<UserTokenType> SupportedTokenTypes { get; }
            = [UserTokenType.UserName];

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
            if (policy.TokenType != UserTokenType.UserName)
            {
                return new ValueTask<CanSatisfyResult>(
                    CanSatisfyResult.No(
                        $"TokenTypeNotSupported (provider handles UserName, policy is {policy.TokenType})."));
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
                    "Username/password identity provider cannot satisfy the supplied user token policy: {0}",
                    satisfied.RejectionReason ?? string.Empty);
            }

            ISecret? secret = await m_registry
                .GetAsync(m_passwordId, ct)
                .ConfigureAwait(false) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenRejected,
                    "Password secret could not be resolved for the selected username policy.");

            try
            {
                return new UserIdentity(m_username, secret.Bytes, context.SecurityPolicyRegistry)
                {
                    PolicyId = policy.PolicyId ?? string.Empty
                };
            }
            finally
            {
                secret.Dispose();
            }
        }

        private readonly string m_username;
        private readonly ISecretRegistry m_registry;
        private readonly SecretIdentifier m_passwordId;
    }
}
