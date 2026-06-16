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
        public bool CanSatisfy(
            UserTokenPolicy policy,
            IdentitySelectionContext context)
        {
            return policy != null && policy.TokenType == UserTokenType.UserName;
        }

        /// <inheritdoc/>
        public async ValueTask<IUserIdentity> GetIdentityAsync(
            UserTokenPolicy policy,
            IdentitySelectionContext context,
            CancellationToken ct = default)
        {
            if (!CanSatisfy(policy, context))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenRejected,
                    "Username/password identity provider can only satisfy username user token policies.");
            }

            ISecret? secret = await m_registry
                .GetAsync(m_passwordId, ct)
                .ConfigureAwait(false) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenRejected,
                    "Password secret could not be resolved for the selected username policy.");

            try
            {
                return new UserIdentity(m_username, secret.Bytes)
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
