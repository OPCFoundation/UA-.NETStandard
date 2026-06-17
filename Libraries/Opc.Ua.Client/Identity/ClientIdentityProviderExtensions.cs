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
using System.Text;
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
        /// The set of client-enabled security policies defaults to the channel security policy carried
        /// on <paramref name="endpointDescription"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="endpointDescription"/> or <paramref name="messageContext"/> is <c>null</c>.
        /// </exception>
        public static ValueTask<IUserIdentity> AcquireIdentityAsync(
            this IClientIdentityProvider provider,
            EndpointDescription endpointDescription,
            IServiceMessageContext messageContext,
            CancellationToken ct = default)
        {
            return AcquireIdentityAsync(
                provider,
                endpointDescription,
                messageContext,
                ArrayOf<string>.Null,
                ct);
        }

        /// <summary>
        /// Selects a matching user-token policy and asks <paramref name="provider"/> to create an identity.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="endpointDescription"/> or <paramref name="messageContext"/> is <c>null</c>.
        /// </exception>
        public static ValueTask<IUserIdentity> AcquireIdentityAsync(
            this IClientIdentityProvider provider,
            EndpointDescription endpointDescription,
            IServiceMessageContext messageContext,
            ArrayOf<string> enabledSecurityPolicyUris,
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
                endpointDescription.UserIdentityTokens,
                messageContext,
                enabledSecurityPolicyUris);
            return provider.AcquireIdentityAsync(context, ct);
        }

        /// <summary>
        /// Selects a matching user-token policy and asks <paramref name="provider"/> to create an identity.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="provider"/> is <c>null</c>.</exception>
        public static async ValueTask<IUserIdentity> AcquireIdentityAsync(
            this IClientIdentityProvider provider,
            IdentitySelectionContext context,
            CancellationToken ct = default)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            UserTokenPolicy identityPolicy = await provider
                .SelectUserTokenPolicyAsync(context, ct)
                .ConfigureAwait(false);
            IUserIdentity identity = await provider
                .GetIdentityAsync(identityPolicy, context, ct)
                .ConfigureAwait(false);
            identity.TokenHandler.Token.PolicyId = identityPolicy.PolicyId;
            return identity;
        }

        /// <summary>
        /// Selects the best offered user-token policy for <paramref name="provider"/>.
        /// </summary>
        /// <remarks>
        /// Each offered <see cref="UserTokenPolicy"/> is evaluated in two stages:
        /// <list type="number">
        /// <item><description>
        /// Policies with a non-empty <see cref="UserTokenPolicy.SecurityPolicyUri"/> that is
        /// not in <see cref="IdentitySelectionContext.EnabledSecurityPolicyUris"/> are skipped
        /// (rejection reason <c>NotEnabledByClient</c>). Policies with an empty
        /// <see cref="UserTokenPolicy.SecurityPolicyUri"/> inherit the channel policy and pass
        /// this filter unconditionally.
        /// </description></item>
        /// <item><description>
        /// Surviving candidates are submitted to
        /// <see cref="IClientIdentityProvider.CanSatisfyAsync"/>. Providers may reject a policy
        /// for token-type, profile-URI, or material-specific reasons (e.g. an X.509 user-cert
        /// public key whose curve does not match the policy's
        /// <see cref="UserTokenPolicy.SecurityPolicyUri"/>).
        /// </description></item>
        /// </list>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="provider"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadIdentityTokenRejected"/> when the endpoint offers no
        /// user-token policy that the provider can satisfy. The message lists each offered policy
        /// together with the reason it was rejected.
        /// </exception>
        public static async ValueTask<UserTokenPolicy> SelectUserTokenPolicyAsync(
            this IClientIdentityProvider provider,
            IdentitySelectionContext context,
            CancellationToken ct = default)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            string tokenSecurityPolicyUri =
                context.EndpointDescription?.SecurityPolicyUri ?? SecurityPolicies.None;
            UserTokenPolicy? sameEncryptionAlgorithm = null;
            UserTokenPolicy? unspecifiedSecPolicy = null;
            var rejections = new List<(UserTokenPolicy Policy, string Reason)>();

            ArrayOf<UserTokenPolicy> offered = context.OfferedPolicies;
            for (int i = 0; i < offered.Count; i++)
            {
                UserTokenPolicy policy = offered[i];
                if (!IsPolicyEnabledByClient(policy, context, out string? notEnabledReason))
                {
                    rejections.Add((policy, notEnabledReason!));
                    continue;
                }

                CanSatisfyResult satisfied = await provider
                    .CanSatisfyAsync(policy, context, ct)
                    .ConfigureAwait(false);
                if (!satisfied.CanSatisfy)
                {
                    rejections.Add((policy, satisfied.RejectionReason ?? "ProviderRejected"));
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

            UserTokenPolicy? selected = sameEncryptionAlgorithm ?? unspecifiedSecPolicy;
            if (selected != null)
            {
                return selected;
            }

            throw ServiceResultException.Create(
                StatusCodes.BadIdentityTokenRejected,
                BuildNoPolicyDiagnostic(context, rejections));
        }

        private static bool IsPolicyEnabledByClient(
            UserTokenPolicy policy,
            IdentitySelectionContext context,
            out string? rejectionReason)
        {
            if (string.IsNullOrEmpty(policy?.SecurityPolicyUri))
            {
                rejectionReason = null;
                return true;
            }

            ArrayOf<string> enabled = context.EnabledSecurityPolicyUris;
            if (enabled.IsNull || enabled.Count == 0)
            {
                rejectionReason = null;
                return true;
            }

            ReadOnlySpan<string> span = enabled.Span;
            for (int i = 0; i < span.Length; i++)
            {
                if (string.Equals(span[i], policy.SecurityPolicyUri, StringComparison.Ordinal))
                {
                    rejectionReason = null;
                    return true;
                }
            }

            rejectionReason = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "NotEnabledByClient (client SupportedSecurityPolicies excludes '{0}').",
                policy.SecurityPolicyUri);
            return false;
        }

        private static string BuildNoPolicyDiagnostic(
            IdentitySelectionContext context,
            List<(UserTokenPolicy Policy, string Reason)> rejections)
        {
            var sb = new StringBuilder();
            sb.Append("Endpoint does not offer a user token policy that can be satisfied by the identity provider.");

            if (rejections == null || rejections.Count == 0)
            {
                sb.Append(" Endpoint advertises no user-token policies.");
                return sb.ToString();
            }

            sb.Append(" Offered policies:");
            foreach ((UserTokenPolicy policy, string reason) in rejections)
            {
                sb.AppendLine();
                sb.Append("  - PolicyId='").Append(policy?.PolicyId ?? string.Empty)
                    .Append("', TokenType=").Append(policy?.TokenType.ToString() ?? "<null>");
                if (!string.IsNullOrEmpty(policy?.SecurityPolicyUri))
                {
                    sb.Append(", SecurityPolicyUri='").Append(policy.SecurityPolicyUri).Append('\'');
                }
                if (!string.IsNullOrEmpty(policy?.IssuedTokenType))
                {
                    sb.Append(", IssuedTokenType='").Append(policy.IssuedTokenType).Append('\'');
                }
                sb.Append(": ").Append(reason);
            }

            ArrayOf<string> enabled = context.EnabledSecurityPolicyUris;
            if (!enabled.IsNull && enabled.Count > 0)
            {
                sb.AppendLine();
                sb.Append("  Client-enabled SecurityPolicyUris: ");
                bool first = true;
                foreach (string uri in enabled)
                {
                    if (!first)
                    {
                        sb.Append(", ");
                    }
                    sb.Append(uri);
                    first = false;
                }
            }

            return sb.ToString();
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
