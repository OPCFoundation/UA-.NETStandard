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
    /// Client identity provider that acquires an issued access token for
    /// OPC UA JWT user-token policies.
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
        public bool CanSatisfy(
            UserTokenPolicy policy,
            IdentitySelectionContext context)
        {
            return policy != null &&
                policy.TokenType == UserTokenType.IssuedToken &&
                string.Equals(policy.IssuedTokenType, m_profileUri, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public async ValueTask<IUserIdentity> GetIdentityAsync(
            UserTokenPolicy policy,
            IdentitySelectionContext context,
            CancellationToken ct = default)
        {
            if (!CanSatisfy(policy, context) ||
                !AuthorizationServerMetadata.TryFromPolicy(policy, out AuthorizationServerMetadata metadata))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenRejected,
                    "Issued-token identity provider requires a JWT user token policy.");
            }

            AccessToken accessToken = await m_accessTokenProvider
                .AcquireAsync(metadata, ct)
                .ConfigureAwait(false);
            try
            {
                byte[] tokenData = accessToken.TokenData.ToArray();
                var identity = new UserIdentity(tokenData, accessToken.ProfileUri)
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
        private readonly object m_lock = new();
        private DateTime m_expiresAt = DateTime.MaxValue;
    }
}
