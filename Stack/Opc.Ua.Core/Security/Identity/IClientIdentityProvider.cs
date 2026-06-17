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
    /// Context passed to an <see cref="IClientIdentityProvider"/> while
    /// the client is negotiating <c>ActivateSession</c>.
    /// </summary>
    public readonly record struct IdentitySelectionContext
    {
        /// <summary>
        /// Creates a context with no client-enabled-policy filter
        /// (every offered <see cref="UserTokenPolicy"/> passes the
        /// <c>NotEnabledByClient</c> gate). Use the four-argument
        /// constructor to apply the
        /// <see cref="SecurityConfiguration.SupportedSecurityPolicies"/>
        /// snapshot.
        /// </summary>
        public IdentitySelectionContext(
            EndpointDescription endpointDescription,
            IReadOnlyList<UserTokenPolicy> offeredPolicies,
            IServiceMessageContext messageContext)
            : this(
                endpointDescription,
                offeredPolicies,
                messageContext,
                Array.Empty<string>())
        {
        }

        /// <summary>
        /// Creates a context with an explicit list of client-enabled
        /// security policies.
        /// </summary>
        public IdentitySelectionContext(
            EndpointDescription endpointDescription,
            IReadOnlyList<UserTokenPolicy> offeredPolicies,
            IServiceMessageContext messageContext,
            IReadOnlyList<string> enabledSecurityPolicyUris)
        {
            EndpointDescription = endpointDescription;
            OfferedPolicies = offeredPolicies;
            MessageContext = messageContext;
            EnabledSecurityPolicyUris = enabledSecurityPolicyUris ?? [];
        }

        /// <summary>
        /// The endpoint the client is connecting to. Carries the
        /// <see cref="EndpointDescription.SecurityPolicyUri"/> and
        /// offered <see cref="EndpointDescription.UserIdentityTokens"/>.
        /// </summary>
        public EndpointDescription EndpointDescription { get; init; }

        /// <summary>
        /// The list of <see cref="UserTokenPolicy"/> values the server
        /// advertised on the endpoint, ordered by server preference.
        /// </summary>
        public IReadOnlyList<UserTokenPolicy> OfferedPolicies { get; init; }

        /// <summary>
        /// The service message context (encoder factories, telemetry).
        /// </summary>
        public IServiceMessageContext MessageContext { get; init; }

        /// <summary>
        /// The set of <see cref="SecurityPolicies"/> URIs the local
        /// client has enabled (typically the
        /// <see cref="SecurityConfiguration.SupportedSecurityPolicies"/>
        /// snapshot). Selection rejects any offered
        /// <see cref="UserTokenPolicy"/> whose non-empty
        /// <see cref="UserTokenPolicy.SecurityPolicyUri"/> is not in
        /// this list, so deprecated or locally-disabled security
        /// policies cannot be used by accident.
        /// </summary>
        public IReadOnlyList<string> EnabledSecurityPolicyUris { get; init; }
    }

    /// <summary>
    /// Result of <see cref="IClientIdentityProvider.CanSatisfyAsync"/>.
    /// Carries a boolean and, when negative, a human-readable reason
    /// that the client extension layer aggregates into the
    /// <c>BadIdentityTokenRejected</c> diagnostic message.
    /// </summary>
    public readonly record struct CanSatisfyResult
    {
        private CanSatisfyResult(bool canSatisfy, string? rejectionReason)
        {
            CanSatisfy = canSatisfy;
            RejectionReason = rejectionReason;
        }

        /// <summary>
        /// True when the provider can produce an identity for the
        /// supplied <see cref="UserTokenPolicy"/>.
        /// </summary>
        public bool CanSatisfy { get; }

        /// <summary>
        /// Reason the provider rejected the policy. Always non-null
        /// when <see cref="CanSatisfy"/> is <see langword="false"/>.
        /// </summary>
        public string? RejectionReason { get; }

        /// <summary>
        /// The provider satisfies the policy.
        /// </summary>
        public static readonly CanSatisfyResult Yes = new(true, null);

        /// <summary>
        /// The provider rejects the policy with the supplied reason.
        /// </summary>
        public static CanSatisfyResult No(string reason)
        {
            return new CanSatisfyResult(
                false,
                string.IsNullOrEmpty(reason)
                    ? "Provider cannot satisfy the policy."
                    : reason);
        }
    }

    /// <summary>
    /// Produces an <see cref="IUserIdentity"/> for use in
    /// <c>ActivateSession</c>. Replaces the historical pattern of
    /// constructing a <see cref="UserIdentity"/> eagerly before opening
    /// a session — the provider is consulted lazily so access-token
    /// acquisition (OAuth2 / OIDC / GDS) only happens when needed and
    /// can be refreshed on long-lived sessions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A provider that supports multiple token types should expose them
    /// through <see cref="SupportedTokenTypes"/> so a composite provider
    /// (introduced in P4) can match against
    /// <see cref="IdentitySelectionContext.OfferedPolicies"/>.
    /// </para>
    /// <para>
    /// Providers MUST be thread-safe: a session reactivation may run
    /// concurrently with a normal service call.
    /// </para>
    /// </remarks>
    public interface IClientIdentityProvider
    {
        /// <summary>
        /// The OPC UA user-token types this provider can fulfil. Used by
        /// composite providers to dispatch by
        /// <see cref="UserTokenPolicy.TokenType"/>.
        /// </summary>
        IReadOnlyList<UserTokenType> SupportedTokenTypes { get; }

        /// <summary>
        /// For <see cref="UserTokenType.IssuedToken"/> providers, the set
        /// of <c>IssuedTokenType</c> profile URIs this provider can
        /// handle. Empty for non-issued-token providers.
        /// </summary>
        IReadOnlyList<string> SupportedIssuedTokenProfileUris { get; }

        /// <summary>
        /// Asks the provider whether it can satisfy
        /// <paramref name="policy"/> in <paramref name="context"/>.
        /// Implementations check
        /// <see cref="SupportedTokenTypes"/> and
        /// <see cref="SupportedIssuedTokenProfileUris"/> at minimum;
        /// providers that need to inspect a backing certificate or
        /// secret to make the decision (e.g. matching an X.509 user
        /// cert's curve against the policy's
        /// <see cref="UserTokenPolicy.SecurityPolicyUri"/>) load the
        /// material here through the appropriate provider abstraction
        /// (<see cref="ICertificateProvider"/>,
        /// <see cref="ISecretRegistry"/>,
        /// <see cref="IAccessTokenProvider"/>).
        /// </summary>
        /// <remarks>
        /// On rejection, return <see cref="CanSatisfyResult.No"/> with
        /// a human-readable reason; the client extension aggregates
        /// these reasons into the diagnostic message of the
        /// <c>BadIdentityTokenRejected</c> exception that is raised
        /// when no offered policy is selectable.
        /// </remarks>
        ValueTask<CanSatisfyResult> CanSatisfyAsync(
            UserTokenPolicy policy,
            IdentitySelectionContext context,
            CancellationToken ct = default);

        /// <summary>
        /// Materialize an <see cref="IUserIdentity"/> for the selected
        /// <paramref name="policy"/>. Implementations resolve passwords
        /// via <see cref="ISecretRegistry"/>, certificates via
        /// <see cref="ICertificateProvider"/>, and access tokens via
        /// <see cref="IAccessTokenProvider"/> as appropriate.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// <c>BadIdentityTokenRejected</c> when the provider cannot
        /// produce a token for the offered policy.
        /// </exception>
        ValueTask<IUserIdentity> GetIdentityAsync(
            UserTokenPolicy policy,
            IdentitySelectionContext context,
            CancellationToken ct = default);

        /// <summary>
        /// UTC instant at which the most recently produced identity
        /// becomes invalid (typically the access-token expiry). Returns
        /// <see cref="DateTime.MaxValue"/> for identities that do not
        /// expire (e.g. <c>Anonymous</c>, <c>UserName</c>, <c>X509</c>).
        /// </summary>
        /// <remarks>
        /// Callers use this to schedule proactive refresh through
        /// <c>Session.UpdateIdentityAsync</c>.
        /// </remarks>
        DateTime ExpiresAt { get; }
    }
}
