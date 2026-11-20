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
    /// A request to issue an access token, used by a server-side
    /// Authorization Service (Part 12 §9) implementation. Carries the
    /// authenticated principal plus the desired audience / scopes.
    /// </summary>
    /// <param name="Subject">
    /// Stable identifier for the authenticated principal (JWT
    /// <c>sub</c> claim).
    /// </param>
    /// <param name="AudienceUri">
    /// The resource URI the issued token is bound to (JWT <c>aud</c>
    /// claim). Typically the target server's <c>ApplicationUri</c>.
    /// </param>
    /// <param name="RequestedScopes">
    /// OAuth2 scopes the caller is requesting. The issuer may grant a
    /// subset.
    /// </param>
    /// <param name="AdditionalClaims">
    /// Extra claims the issuer should embed into the token (for example
    /// <c>groups</c> or <c>roles</c> propagated from a chained identity
    /// provider per Part 12 §9.5 "Chained Authorization").
    /// </param>
    /// <param name="RequestedLifetime">
    /// Desired token lifetime. The issuer may shorten this.
    /// </param>
    public readonly record struct TokenIssuanceRequest(
        string Subject,
        string AudienceUri,
        IReadOnlyList<string> RequestedScopes,
        IReadOnlyDictionary<string, object?> AdditionalClaims,
        TimeSpan RequestedLifetime);

    /// <summary>
    /// Issues access tokens on behalf of an Authorization Service
    /// (Part 12 §9). Plug-in extension point for the GDS implementation:
    /// the default in-box implementation signs JWTs with an ECDSA key
    /// resident in the GDS, but a deployment can substitute a corporate
    /// CA or a cloud KMS by implementing this interface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The interface is async because real-world issuers (HSMs, cloud
    /// KMSs, remote signing services) cannot be assumed to be
    /// in-process. The default ECDSA-with-resident-key implementation
    /// completes synchronously.
    /// </para>
    /// <para>
    /// Used in P6 to back the modern Part 12 v1.05
    /// <c>StartRequestToken</c> / <c>FinishRequestToken</c> server-side
    /// flow. The deprecated <c>RequestAccessToken</c> method remains
    /// available but uses the same <see cref="ITokenIssuer"/> under the
    /// hood.
    /// </para>
    /// </remarks>
    public interface ITokenIssuer
    {
        /// <summary>
        /// Issuer URI placed in the token's <c>iss</c> claim.
        /// </summary>
        string IssuerUri { get; }

        /// <summary>
        /// OPC UA issued-token profile URI describing the token format
        /// produced by this issuer (e.g.
        /// <see cref="Profiles.JwtUserToken"/>).
        /// </summary>
        string ProfileUri { get; }

        /// <summary>
        /// Issues an access token. The returned <see cref="AccessToken"/>
        /// is owned by the caller and must be disposed.
        /// </summary>
        ValueTask<AccessToken> IssueAsync(
            TokenIssuanceRequest request,
            CancellationToken ct = default);
    }
}
