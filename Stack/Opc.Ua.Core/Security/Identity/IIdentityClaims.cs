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

using System.Collections.Generic;

namespace Opc.Ua.Identity
{
    /// <summary>
    /// Optional probe interface that exposes authentication claims (groups,
    /// roles, raw key/value pairs) carried by an authenticated
    /// <see cref="IUserIdentity"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Designed to be implemented alongside <see cref="IUserIdentity"/>
    /// (NOT inherited) so that existing identity types remain on the
    /// stable, non-claims contract. Consumers probe via
    /// <c>identity as IIdentityClaims</c>.
    /// </para>
    /// <para>
    /// Implementations populate this from the underlying token: a JWT
    /// <c>IssuedIdentityToken</c> exposes the decoded JWT claim set; an
    /// <c>X509IdentityToken</c> typically projects subject + SAN + EKU; a
    /// <c>UserNameIdentityToken</c> projects <c>preferred_username</c>.
    /// </para>
    /// <para>
    /// This interface intentionally has no setters. Claims are populated
    /// once by the validating <see cref="IUserTokenAuthenticator"/> and
    /// are immutable for the lifetime of the identity.
    /// </para>
    /// <para>
    /// Used by <c>IRoleManager.ResolveGrantedRoles</c> to evaluate the
    /// Part 18 §4.4.4 <c>GroupId</c> and <c>Role</c> identity criteria
    /// against externally asserted group/role claims (e.g. from an
    /// OIDC <c>groups</c> / <c>roles</c> claim, an Entra <c>groups</c>
    /// claim, or a Kerberos PAC).
    /// </para>
    /// </remarks>
    public interface IIdentityClaims
    {
        /// <summary>
        /// The raw claim set keyed by claim name. Claim names follow
        /// OIDC / Entra conventions where possible — for example
        /// <c>sub</c>, <c>iss</c>, <c>aud</c>, <c>exp</c>,
        /// <c>preferred_username</c>, <c>email</c>, <c>oid</c>,
        /// <c>tid</c>, <c>upn</c>, <c>groups</c>, <c>roles</c>,
        /// <c>scope</c>.
        /// </summary>
        /// <remarks>
        /// Values are typically <see cref="string"/>, <see cref="long"/>,
        /// <see cref="bool"/>, or <see cref="IReadOnlyList{T}"/> of those.
        /// JSON arrays from a JWT body are projected as
        /// <see cref="IReadOnlyList{T}"/> of <see cref="string"/>;
        /// nested JSON objects are not exposed in this interface and
        /// should be flattened by the authenticator using dot notation
        /// when needed.
        /// </remarks>
        IReadOnlyDictionary<string, object?> Claims { get; }

        /// <summary>
        /// Group identifiers asserted by the identity provider, typically
        /// the values of the OIDC / Entra <c>groups</c> claim. Used to
        /// evaluate Part 18 §4.4.4 <see cref="IdentityCriteriaType.GroupId"/>.
        /// </summary>
        /// <remarks>
        /// May be empty when the identity provider did not assert group
        /// membership. Implementations that source groups from multiple
        /// claims (e.g. Entra <c>groups</c> plus a custom
        /// <c>group_ids</c> claim) merge them into this list.
        /// </remarks>
        IReadOnlyList<string> Groups { get; }

        /// <summary>
        /// Role identifiers asserted by the identity provider, typically
        /// the values of the OIDC / Entra <c>roles</c> claim. Used to
        /// evaluate Part 18 §4.4.4 <see cref="IdentityCriteriaType.Role"/>.
        /// </summary>
        /// <remarks>
        /// Per Part 18 §4.4.4 a <see cref="IdentityCriteriaType.Role"/>
        /// criteria value may be prefixed with the issuer URI separated
        /// by a forward slash (<c>iss/roleName</c>). The
        /// <c>IRoleManager</c> consumer of this list is responsible for
        /// resolving that prefix against
        /// <see cref="Claims"/>[<c>iss</c>].
        /// </remarks>
        IReadOnlyList<string> Roles { get; }

        /// <summary>
        /// Issuer URI as asserted by the identity provider, mirroring the
        /// JWT <c>iss</c> claim. Provided as a convenience for Part 18
        /// role-claim prefix resolution and audit logging. May be
        /// <see langword="null"/> when the underlying token type does
        /// not carry an issuer (e.g. an X509 certificate identity).
        /// </summary>
        string? Issuer { get; }

        /// <summary>
        /// The subject identifier asserted by the identity provider,
        /// mirroring the JWT <c>sub</c> claim. For X509 identities this
        /// is the canonical subject DN; for UserName identities this is
        /// the user name. Used as a stable identity key for audit
        /// logging.
        /// </summary>
        string? Subject { get; }
    }
}
