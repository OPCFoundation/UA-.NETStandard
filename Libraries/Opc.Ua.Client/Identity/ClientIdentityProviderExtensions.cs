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

#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Identity
{
    /// <summary>
    /// Helper APIs for materializing client user identities from endpoint policies.
    /// </summary>
    public static class ClientIdentityProviderExtensions
    {
        /// <summary>
        /// Selects a matching user-token policy and asks <paramref name="provider"/> to create an identity.
        /// </summary>
        public static ValueTask<IUserIdentity> AcquireIdentityAsync(
            this IClientIdentityProvider provider,
            EndpointDescription endpointDescription,
            IServiceMessageContext messageContext,
            CancellationToken ct = default)
        {
            if (endpointDescription == null)
            {
                throw new ArgumentNullException(nameof(endpointDescription));
            }
            if (messageContext == null)
            {
                throw new ArgumentNullException(nameof(messageContext));
            }

            var context = new IdentitySelectionContext(
                endpointDescription,
                endpointDescription.UserIdentityTokens.ToArray() ?? Array.Empty<UserTokenPolicy>(),
                messageContext);
            return provider.AcquireIdentityAsync(context, ct);
        }

        /// <summary>
        /// Selects a matching user-token policy and asks <paramref name="provider"/> to create an identity.
        /// </summary>
        public static async ValueTask<IUserIdentity> AcquireIdentityAsync(
            this IClientIdentityProvider provider,
            IdentitySelectionContext context,
            CancellationToken ct = default)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            UserTokenPolicy identityPolicy = provider.SelectUserTokenPolicy(context);
            IUserIdentity identity = await provider
                .GetIdentityAsync(identityPolicy, context, ct)
                .ConfigureAwait(false);
            identity.TokenHandler.Token.PolicyId = identityPolicy.PolicyId;
            return identity;
        }

        /// <summary>
        /// Selects the best offered user-token policy for <paramref name="provider"/>.
        /// </summary>
        public static UserTokenPolicy SelectUserTokenPolicy(
            this IClientIdentityProvider provider,
            IdentitySelectionContext context)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            string tokenSecurityPolicyUri =
                context.EndpointDescription.SecurityPolicyUri ?? SecurityPolicies.None;
            UserTokenPolicy? sameEncryptionAlgorithm = null;
            UserTokenPolicy? unspecifiedSecPolicy = null;

            foreach (UserTokenPolicy policy in context.OfferedPolicies)
            {
                if (!provider.CanSatisfy(policy, context))
                {
                    continue;
                }

                if (policy.TokenType == UserTokenType.Anonymous ||
                    string.Equals(
                        policy.SecurityPolicyUri,
                        tokenSecurityPolicyUri,
                        StringComparison.Ordinal))
                {
                    return policy;
                }

                if (string.IsNullOrEmpty(policy.SecurityPolicyUri))
                {
                    unspecifiedSecPolicy ??= policy;
                }
                else if (HasSameEncryptionAlgorithm(
                    policy.SecurityPolicyUri!,
                    tokenSecurityPolicyUri))
                {
                    sameEncryptionAlgorithm ??= policy;
                }
            }

            return sameEncryptionAlgorithm ?? unspecifiedSecPolicy ??
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenRejected,
                    "Endpoint does not offer a user token policy that can be satisfied by the identity provider.");
        }

        private static bool HasSameEncryptionAlgorithm(
            string policySecurityPolicyUri,
            string tokenSecurityPolicyUri)
        {
            return CryptoUtils.IsEccPolicy(policySecurityPolicyUri) ==
                CryptoUtils.IsEccPolicy(tokenSecurityPolicyUri);
        }
    }
}
