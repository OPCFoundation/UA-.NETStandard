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
    /// <param name="EndpointDescription">
    /// The endpoint the client is connecting to. Carries the
    /// <see cref="EndpointDescription.SecurityPolicyUri"/> and offered
    /// <see cref="EndpointDescription.UserIdentityTokens"/>.
    /// </param>
    /// <param name="OfferedPolicies">
    /// The list of <see cref="UserTokenPolicy"/> values the server
    /// advertised on the endpoint, ordered by server preference.
    /// </param>
    /// <param name="MessageContext">
    /// The service message context (encoder factories, telemetry).
    /// </param>
    public readonly record struct IdentitySelectionContext(
        EndpointDescription EndpointDescription,
        IReadOnlyList<UserTokenPolicy> OfferedPolicies,
        IServiceMessageContext MessageContext);

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
        /// <paramref name="policy"/> in <paramref name="context"/>. The
        /// default implementation checks
        /// <see cref="SupportedTokenTypes"/> and
        /// <see cref="SupportedIssuedTokenProfileUris"/>; bespoke
        /// providers can override to factor in scope/audience/policy id.
        /// </summary>
        bool CanSatisfy(UserTokenPolicy policy, IdentitySelectionContext context);

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
