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

#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Opc.Ua.Bindings.WebApi
{
    /// <summary>
    /// Options for <see cref="JwtClaimSessionlessIdentityProvider"/>.
    /// </summary>
    /// <remarks>
    /// All claim names default to the standard OAuth 2.0 / OpenID Connect
    /// JWT claim names (RFC 7519 + RFC 8693 for <c>scope</c>). Callers
    /// that need to project non-standard claims can override the names
    /// or supply a custom <see cref="TransformIdentity"/> hook.
    /// </remarks>
    public sealed class JwtClaimSessionlessIdentityProviderOptions
    {
        /// <summary>
        /// Name of the claim that supplies the subject identifier.
        /// Defaults to <c>"sub"</c> (RFC 7519 §4.1.2).
        /// </summary>
        public string SubjectClaim { get; set; } = "sub";

        /// <summary>
        /// Name of the claim that carries OAuth 2.0 scopes. Defaults to
        /// <c>"scope"</c> (RFC 8693). The claim value is expected to be
        /// a space-separated list of scope tokens; the provider splits
        /// it into individual entries surfaced via
        /// <c>JwtClaimSessionlessIdentityProvider.GetScopes</c>.
        /// </summary>
        public string ScopeClaim { get; set; } = "scope";

        /// <summary>
        /// Name of the claim that carries the role memberships.
        /// Defaults to <c>"roles"</c>. Recognises both repeated claim
        /// instances and a single comma-separated value.
        /// </summary>
        public string RolesClaim { get; set; } = "roles";

        /// <summary>
        /// When <c>true</c>, returns an explicit anonymous
        /// <see cref="UserIdentity"/> for unauthenticated requests
        /// instead of <c>null</c>. The two are equivalent at the
        /// dispatcher boundary but the explicit form helps downstream
        /// audit logging distinguish "no identity supplied" from
        /// "anonymous identity supplied".
        /// </summary>
        public bool ReturnAnonymousForUnauthenticated { get; set; }

        /// <summary>
        /// Optional transform applied to the projected
        /// <see cref="UserIdentity"/> before it is returned. Useful for
        /// callers that want to attach diagnostic <c>AdditionalParameters</c>
        /// or remap the projection without subclassing the provider.
        /// </summary>
        public Func<HttpContext, ClaimsPrincipal, IUserIdentity, IUserIdentity>? TransformIdentity { get; set; }
    }

    /// <summary>
    /// <see cref="ISessionlessIdentityProvider"/> that projects JWT
    /// principals (populated upstream by
    /// <c>Microsoft.AspNetCore.Authentication.JwtBearer</c>) into OPC UA
    /// <see cref="UserIdentity"/> objects carrying the original bearer
    /// token as an <see cref="Profiles.JwtUserToken"/> issued-token
    /// payload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The provider intentionally does NOT re-validate the JWT (the
    /// upstream bearer middleware already did) and does NOT hook into
    /// the <c>RoleBasedUserManagement</c> infrastructure (that's a
    /// separate, larger design). Its only responsibility is to surface
    /// the JWT's standard claims (<c>sub</c> / <c>scope</c> /
    /// <c>roles</c>) on the OPC UA identity model so server-side
    /// authorization can read them.
    /// </para>
    /// <para>
    /// The original raw bearer token is recovered from the inbound
    /// <c>Authorization: Bearer …</c> request header and passed to
    /// <see cref="UserIdentity(ReadOnlySpan{byte}, string)"/> so the
    /// server-side dispatcher can re-introduce the JWT into the
    /// <see cref="RequestHeader.AuthenticationToken"/> chain if it
    /// needs to. The <see cref="GetScopes(IUserIdentity)"/> /
    /// <see cref="GetRoles(IUserIdentity)"/> helpers parse the projected
    /// claims for downstream consumers.
    /// </para>
    /// </remarks>
    public sealed class JwtClaimSessionlessIdentityProvider : ISessionlessIdentityProvider
    {
        private const string ScopeSeparator = " ";
        private static readonly char[] s_roleSeparators = [',', ';', ' '];

        /// <summary>
        /// Diagnostic context keys used to stash the projected scopes /
        /// roles / subject on the returned UserIdentity. Surfaced via the
        /// static GetScopes/GetRoles/GetSubject helpers so downstream code
        /// doesn't need to know the storage shape.
        /// </summary>
        internal const string SubjectKey = "JwtSubject";
        internal const string ScopesKey = "JwtScopes";
        internal const string RolesKey = "JwtRoles";

        private readonly JwtClaimSessionlessIdentityProviderOptions m_options;

        /// <summary>
        /// Initializes a new provider with default claim names
        /// (<c>"sub"</c> / <c>"scope"</c> / <c>"roles"</c>).
        /// </summary>
        public JwtClaimSessionlessIdentityProvider()
            : this(new JwtClaimSessionlessIdentityProviderOptions())
        {
        }

        /// <summary>
        /// Initializes a new provider with the supplied options.
        /// </summary>
        /// <param name="options">Configuration.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="options"/> is <c>null</c>.
        /// </exception>
        public JwtClaimSessionlessIdentityProvider(JwtClaimSessionlessIdentityProviderOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public IUserIdentity? Resolve(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            ClaimsPrincipal principal = context.User;
            if (principal?.Identity is not { IsAuthenticated: true })
            {
                return m_options.ReturnAnonymousForUnauthenticated ? new UserIdentity() : null;
            }

            string subject = principal.FindFirst(m_options.SubjectClaim)?.Value
                ?? principal.Identity.Name
                ?? string.Empty;

            // Recover the raw JWT from the Authorization: Bearer header.
            // The bearer middleware doesn't store the token on the principal
            // by default, so we re-read it from the request.
            byte[] tokenBytes = ExtractRawBearerTokenBytes(context.Request);

            IUserIdentity identity;
            if (tokenBytes.Length > 0)
            {
                identity = new UserIdentity(tokenBytes, Profiles.JwtUserToken);
            }
            else
            {
                // No raw token recoverable — covers principals synthesised
                // by tests / custom middleware. We never forge a UserName
                // token with an empty password here: a server that trusts
                // the username without re-verifying the password would
                // let any authenticated principal impersonate any user.
                // Return anonymous (or null per options); the upstream
                // subject still flows through
                // SecureChannelContext.UpstreamIdentity so callers that
                // need it can recover it from there.
                return m_options.ReturnAnonymousForUnauthenticated ? new UserIdentity() : null;
            }

            IReadOnlyList<string> scopes = ExtractScopes(principal, m_options.ScopeClaim);
            IReadOnlyList<string> roles = ExtractRoles(principal, m_options.RolesClaim);

            ProjectionContext.Attach(identity, subject, scopes, roles);

            if (m_options.TransformIdentity is { } transform)
            {
                identity = transform(context, principal, identity)
                    ?? throw new InvalidOperationException(
                        "JwtClaimSessionlessIdentityProviderOptions.TransformIdentity returned null.");
            }
            return identity;
        }

        /// <summary>
        /// Returns the JWT subject claim that this provider projected
        /// onto the supplied identity, or an empty string when the
        /// identity was not produced by
        /// <see cref="JwtClaimSessionlessIdentityProvider"/>.
        /// </summary>
        /// <param name="identity">The identity to inspect.</param>
        public static string GetSubject(IUserIdentity? identity)
        {
            return ProjectionContext.GetSubject(identity);
        }

        /// <summary>
        /// Returns the JWT <c>scope</c> claim entries that this provider
        /// projected onto the supplied identity, or an empty list when
        /// the identity was not produced by
        /// <see cref="JwtClaimSessionlessIdentityProvider"/>.
        /// </summary>
        /// <param name="identity">The identity to inspect.</param>
        public static IReadOnlyList<string> GetScopes(IUserIdentity? identity)
        {
            return ProjectionContext.GetScopes(identity);
        }

        /// <summary>
        /// Returns the JWT <c>roles</c> claim entries that this provider
        /// projected onto the supplied identity, or an empty list when
        /// the identity was not produced by
        /// <see cref="JwtClaimSessionlessIdentityProvider"/>.
        /// </summary>
        /// <param name="identity">The identity to inspect.</param>
        public static IReadOnlyList<string> GetRoles(IUserIdentity? identity)
        {
            return ProjectionContext.GetRoles(identity);
        }

        private static byte[] ExtractRawBearerTokenBytes(HttpRequest request)
        {
            string? authHeader = request.Headers[HeaderNames.Authorization];
            if (string.IsNullOrEmpty(authHeader))
            {
                return [];
            }
            const string prefix = "Bearer ";
            if (!authHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return [];
            }
            string token = authHeader[prefix.Length..].Trim();
            if (token.Length == 0)
            {
                return [];
            }
            return Encoding.UTF8.GetBytes(token);
        }

        private static List<string> ExtractScopes(ClaimsPrincipal principal, string claimName)
        {
            // Scope can appear (a) as a single space-separated claim
            // value (the OAuth 2.0 / RFC 6749 wire shape) or (b) as
            // repeated claim instances (some IdPs split on issue).
            // Accept both shapes.
            var values = new List<string>();
            foreach (Claim claim in principal.FindAll(claimName))
            {
                if (string.IsNullOrEmpty(claim.Value))
                {
                    continue;
                }
                if (claim.Value.Contains(' ', StringComparison.Ordinal))
                {
                    foreach (string piece in claim.Value.Split(
                        ScopeSeparator,
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        values.Add(piece);
                    }
                }
                else
                {
                    values.Add(claim.Value);
                }
            }
            return values;
        }

        private static List<string> ExtractRoles(ClaimsPrincipal principal, string claimName)
        {
            var values = new List<string>();
            foreach (Claim claim in principal.FindAll(claimName))
            {
                if (string.IsNullOrEmpty(claim.Value))
                {
                    continue;
                }
                if (claim.Value.IndexOfAny(s_roleSeparators) >= 0)
                {
                    foreach (string piece in claim.Value.Split(
                        s_roleSeparators,
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        values.Add(piece);
                    }
                }
                else
                {
                    values.Add(claim.Value);
                }
            }
            // Also accept the standard ClaimTypes.Role projection (some
            // JWT middleware maps "roles" claims into ClaimTypes.Role).
            if (!string.Equals(claimName, ClaimTypes.Role, StringComparison.Ordinal))
            {
                foreach (Claim claim in principal.FindAll(ClaimTypes.Role))
                {
                    if (!string.IsNullOrEmpty(claim.Value) && !values.Contains(claim.Value))
                    {
                        values.Add(claim.Value);
                    }
                }
            }
            return values;
        }

        /// <summary>
        /// Conditionally-weak storage mapping IUserIdentity instances to
        /// their projected (subject, scopes, roles) tuples. Surfaced via
        /// the static helpers so downstream code does not need to know
        /// the underlying storage shape.
        /// </summary>
        private static class ProjectionContext
        {
            private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IUserIdentity, Projection> s_table
                = [];

            public static void Attach(
                IUserIdentity identity,
                string subject,
                IReadOnlyList<string> scopes,
                IReadOnlyList<string> roles)
            {
                s_table.AddOrUpdate(identity, new Projection(subject, scopes, roles));
            }

            public static string GetSubject(IUserIdentity? identity)
            {
                return identity != null && s_table.TryGetValue(identity, out Projection? p)
                    ? p.Subject
                    : string.Empty;
            }

            public static IReadOnlyList<string> GetScopes(IUserIdentity? identity)
            {
                return identity != null && s_table.TryGetValue(identity, out Projection? p)
                    ? p.Scopes
                    : Array.Empty<string>();
            }

            public static IReadOnlyList<string> GetRoles(IUserIdentity? identity)
            {
                return identity != null && s_table.TryGetValue(identity, out Projection? p)
                    ? p.Roles
                    : Array.Empty<string>();
            }

            private sealed record Projection(
                string Subject,
                IReadOnlyList<string> Scopes,
                IReadOnlyList<string> Roles);
        }
    }
}
#endif
