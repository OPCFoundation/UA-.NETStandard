/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;

namespace Opc.Ua.Gds.Server.Hosting
{
    /// <summary>
    /// Options for the GDS AuthorizationService JWT issuer.
    /// </summary>
    public sealed class AuthorizationServiceOptions
    {
        /// <summary>
        /// Gets or sets the certificate used to sign issued JWTs. When not
        /// set, the hosted GDS falls back to its application instance
        /// certificate.
        /// </summary>
        public CertificateIdentifier? SigningCertificate { get; set; }

        /// <summary>
        /// Gets or sets the JWT <c>iss</c> value. When empty, the hosted
        /// GDS uses its application URI.
        /// </summary>
        public string IssuerUri { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the lifetime used when the request does not ask for
        /// a shorter lifetime.
        /// </summary>
        public TimeSpan DefaultTokenLifetime { get; set; } = TimeSpan.FromMinutes(60);

        /// <summary>
        /// Gets or sets a value indicating whether the in-memory provider issues
        /// refresh tokens during FinishRequestToken and accepts them on RefreshToken.
        /// Defaults to <c>true</c>. Setting to <c>false</c> preserves the legacy
        /// behaviour where Finish returns no refresh token and Refresh throws
        /// BadNotSupported.
        /// </summary>
        public bool EnableRefreshTokens { get; set; } = true;

        /// <summary>
        /// Gets or sets the lifetime applied to each newly-issued refresh token.
        /// Refresh tokens slide — each successful refresh extends the lifetime
        /// of the new token by this value. OPC 10000-12 does not mandate a
        /// value; the default is 7 days per OAuth2 norms.
        /// </summary>
        public TimeSpan DefaultRefreshTokenLifetime { get; set; } = TimeSpan.FromDays(7);

        /// <summary>
        /// Gets the audience URIs that may receive tokens. When empty, the
        /// default implementation accepts the requested resource URI.
        /// </summary>
        public IList<string> AllowedAudiences { get; } = [];

        /// <summary>
        /// Gets the scopes granted when the caller did not request scopes.
        /// </summary>
        public IList<string> DefaultScopes { get; } = [];

        /// <summary>
        /// Gets or sets an optional authorization predicate over caller,
        /// audience and requested scopes.
        /// </summary>
        public Func<IUserIdentity?, string, IReadOnlyList<string>, bool>? AccessControl { get; set; }

        /// <summary>
        /// Gets or sets an optional authorization callback that decides which of
        /// the roles requested during FinishRequestToken may be embedded into
        /// the issued token. It receives the identity that was authenticated on
        /// the session when the request was started, the audience and the
        /// requested roles, and returns the subset of roles to grant.
        /// </summary>
        /// <remarks>
        /// When this callback is not set no roles are granted (fail-closed).
        /// A caller can therefore never obtain a token asserting privileged
        /// roles (such as SecurityAdmin) that the operator has not explicitly
        /// authorized, which prevents privilege escalation through arbitrary
        /// role requests. The returned roles are always intersected with the
        /// requested roles, so the callback can only ever narrow the set.
        /// </remarks>
        public Func<IUserIdentity?, string, IReadOnlyList<string>, IReadOnlyList<string>>? AuthorizeRoles { get; set; }
    }
}
