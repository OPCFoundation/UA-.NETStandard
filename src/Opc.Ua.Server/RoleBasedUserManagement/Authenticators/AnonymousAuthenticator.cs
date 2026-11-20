// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Identity;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Accepts anonymous user identity tokens.
    /// </summary>
    public sealed class AnonymousAuthenticator : IUserTokenAuthenticator
    {
        /// <inheritdoc/>
        public UserTokenType TokenType => UserTokenType.Anonymous;

        /// <inheritdoc/>
        public string? IssuedTokenProfileUri => null;

        /// <inheritdoc/>
        public ValueTask<AuthenticationResult> AuthenticateAsync(
            AuthenticationContext context,
            CancellationToken ct = default)
        {
            if (context.TokenHandler is not AnonymousIdentityTokenHandler)
            {
                return new ValueTask<AuthenticationResult>(AuthenticationResult.NotHandled);
            }

            return new ValueTask<AuthenticationResult>(
                AuthenticationResult.Accept(new UserIdentity(context.TokenHandler)));
        }
    }
}
